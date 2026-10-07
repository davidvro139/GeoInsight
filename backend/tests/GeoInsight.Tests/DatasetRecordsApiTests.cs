using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Common;
using GeoInsight.Application.Datasets;
using GeoInsight.Application.Imports;
using GeoInsight.Application.Organizations;
using GeoInsight.Application.Records;

namespace GeoInsight.Tests;

/// <summary>
/// Targets query-string binding (page/pageSize/sortBy/sortDir) through the
/// real HTTP pipeline — another boundary unit tests calling DatasetRecordService
/// directly can't see, same reasoning as the other *ApiTests classes.
/// </summary>
public class DatasetRecordsApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string SampleCsv =
        "address_line_1,city,state,latitude,longitude,amount\n" +
        "100 Main St,Ogden,UT,41.2230,-111.9738,150000\n" +
        "200 Oak Ave,Logan,UT,41.7370,-111.8338,200000\n" +
        "300 Pine Rd,Layton,UT,,,175000\n";

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123", "Records Tester"));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private async Task<(int OrgId, int DatasetId)> SeedDatasetWithRecordsAsync(HttpClient client)
    {
        var org = await (await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        var dataset = await (await client.PostAsJsonAsync(
                $"/api/organizations/{org!.Id}/datasets", new CreateDatasetRequest("Loans", null, null)))
            .Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(SampleCsv));
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
    public async Task Points_only_returns_records_with_coordinates()
    {
        var client = await CreateAuthenticatedClientAsync("records-points@example.com");
        var (orgId, datasetId) = await SeedDatasetWithRecordsAsync(client);

        var response = await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/records/points");
        response.EnsureSuccessStatusCode();
        var points = await response.Content.ReadFromJsonAsync<List<DatasetRecordPointDto>>(JsonOptions);

        Assert.Equal(2, points!.Count);
    }

    [Fact]
    public async Task Paged_records_respect_page_and_pageSize_query_parameters()
    {
        var client = await CreateAuthenticatedClientAsync("records-paged@example.com");
        var (orgId, datasetId) = await SeedDatasetWithRecordsAsync(client);

        var response = await client.GetAsync(
            $"/api/organizations/{orgId}/datasets/{datasetId}/records?page=1&pageSize=2&sortBy=amount&sortDir=asc");
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PagedResult<DatasetRecordListItemDto>>(JsonOptions);

        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(150000m, page.Items[0].Amount);
    }
}
