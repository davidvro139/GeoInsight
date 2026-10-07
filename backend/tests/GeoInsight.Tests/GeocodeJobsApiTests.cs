using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Datasets;
using GeoInsight.Application.Geocoding;
using GeoInsight.Application.Imports;
using GeoInsight.Application.Organizations;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Tests;

/// <summary>
/// HTTP-level create/list/get/cancel + permissions. Doesn't assert a job
/// actually completes — no Hangfire server runs inside the test process (see
/// TestApiFactory's Hangfire.MemoryStorage swap) — that's GeocodeJobRunnerTests'
/// job. This is specifically for the HTTP/JSON boundary those tests can't see.
/// </summary>
public class GeocodeJobsApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123", "Geocode Tester"));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private async Task<(int OrgId, int DatasetId)> SeedDatasetWithAPendingRecordAsync(HttpClient client)
    {
        var org = await (await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        var dataset = await (await client.PostAsJsonAsync(
                $"/api/organizations/{org!.Id}/datasets", new CreateDatasetRequest("Loans", null, null)))
            .Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);

        const string csv = "address_line_1,city,state\n100 Main St,Ogden,UT\n";
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "loans.csv");
        var upload = await (await client.PostAsync(
                $"/api/organizations/{org.Id}/datasets/{dataset!.Id}/imports", content))
            .Content.ReadFromJsonAsync<ImportUploadResult>(JsonOptions);

        var mappings = upload!.Headers.Select(h => new ColumnMapping(h, h)).ToList();
        await client.PostAsJsonAsync(
            $"/api/organizations/{org.Id}/datasets/{dataset.Id}/imports/{upload.ImportId}/commit",
            new ImportValidateRequest(upload.DetectedDelimiter, mappings),
            JsonOptions);

        return ((int)org.Id, (int)dataset.Id);
    }

    [Fact]
    public async Task Create_list_get_and_cancel_all_work_through_the_real_pipeline()
    {
        var client = await CreateAuthenticatedClientAsync("geocode-flow@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAPendingRecordAsync(client);

        var createResponse = await client.PostAsync($"/api/organizations/{orgId}/datasets/{datasetId}/geocode-jobs", null);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<GeocodeJobDto>(JsonOptions);
        Assert.Equal("Queued", created!.Status);

        var getResponse = await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/geocode-jobs/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/geocode-jobs");
        var jobs = await listResponse.Content.ReadFromJsonAsync<List<GeocodeJobDto>>(JsonOptions);
        Assert.Single(jobs!);

        var cancelResponse = await client.PostAsync(
            $"/api/organizations/{orgId}/datasets/{datasetId}/geocode-jobs/{created.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var afterCancel = await (await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/geocode-jobs/{created.Id}"))
            .Content.ReadFromJsonAsync<GeocodeJobDto>(JsonOptions);
        Assert.Equal("Cancelled", afterCancel!.Status);
    }

    [Fact]
    public async Task A_viewer_gets_403_starting_a_geocode_job()
    {
        var owner = await CreateAuthenticatedClientAsync("geocode-owner@example.com");
        var viewer = await CreateAuthenticatedClientAsync("geocode-viewer@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAPendingRecordAsync(owner);
        await owner.PostAsJsonAsync(
            $"/api/organizations/{orgId}/members",
            new AddMemberRequest("geocode-viewer@example.com", OrganizationRole.Viewer),
            JsonOptions);

        var response = await viewer.PostAsync($"/api/organizations/{orgId}/datasets/{datasetId}/geocode-jobs", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
