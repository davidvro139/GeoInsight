using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Datasets;
using GeoInsight.Application.Imports;
using GeoInsight.Application.Organizations;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Tests;

/// <summary>
/// Specifically targets the real multipart/form-data upload boundary — a real
/// IFormFile model-binding path unit tests calling ImportService directly
/// never touch — same "HTTP boundary can hide bugs unit tests can't" reasoning
/// as OrganizationsApiTests/DatasetsApiTests.
/// </summary>
public class ImportsApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string SampleCsv =
        "external_id,address_line_1,city,state,postal_code\n" +
        "EXT1,123 Main St,Ogden,UT,84401\n" +
        "EXT2,456 Oak Ave,Logan,UT,84321\n";

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string displayName)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123", displayName));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private static MultipartFormDataContent BuildUploadContent(string csv, string fileName = "loans.csv")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task Full_upload_validate_commit_flow_inserts_dataset_records()
    {
        var client = await CreateAuthenticatedClientAsync("import-flow@example.com", "Importer");
        var org = await (await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        var dataset = await (await client.PostAsJsonAsync(
                $"/api/organizations/{org!.Id}/datasets", new CreateDatasetRequest("Loans", null, null)))
            .Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);

        var uploadResponse = await client.PostAsync(
            $"/api/organizations/{org.Id}/datasets/{dataset!.Id}/imports", BuildUploadContent(SampleCsv));
        uploadResponse.EnsureSuccessStatusCode();
        var upload = await uploadResponse.Content.ReadFromJsonAsync<ImportUploadResult>(JsonOptions);

        Assert.Equal(2, upload!.TotalRows);
        Assert.Equal(",", upload.DetectedDelimiter);
        Assert.Contains("external_id", upload.Headers);

        var mappings = upload.Headers.Select(h => new ColumnMapping(h, h)).ToList();

        var validateResponse = await client.PostAsJsonAsync(
            $"/api/organizations/{org.Id}/datasets/{dataset.Id}/imports/{upload.ImportId}/validate",
            new ImportValidateRequest(upload.DetectedDelimiter, mappings),
            JsonOptions);
        validateResponse.EnsureSuccessStatusCode();
        var validated = await validateResponse.Content.ReadFromJsonAsync<ImportSummaryDto>(JsonOptions);
        Assert.Equal(2, validated!.ValidRows);
        Assert.Equal("Validated", validated.Status);

        var commitResponse = await client.PostAsJsonAsync(
            $"/api/organizations/{org.Id}/datasets/{dataset.Id}/imports/{upload.ImportId}/commit",
            new ImportValidateRequest(upload.DetectedDelimiter, mappings),
            JsonOptions);
        commitResponse.EnsureSuccessStatusCode();
        var committed = await commitResponse.Content.ReadFromJsonAsync<ImportSummaryDto>(JsonOptions);
        Assert.Equal("Committed", committed!.Status);

        var listResponse = await client.GetAsync($"/api/organizations/{org.Id}/datasets/{dataset.Id}/imports");
        var imports = await listResponse.Content.ReadFromJsonAsync<List<ImportListItemDto>>(JsonOptions);
        Assert.Single(imports!);
        Assert.Equal(2, imports![0].ValidRows);
    }

    [Fact]
    public async Task A_viewer_gets_403_uploading_a_file()
    {
        var admin = await CreateAuthenticatedClientAsync("import-owner@example.com", "Owner");
        var viewer = await CreateAuthenticatedClientAsync("import-viewer@example.com", "Viewer");

        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        await admin.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/members",
            new AddMemberRequest("import-viewer@example.com", OrganizationRole.Viewer),
            JsonOptions);
        var dataset = await (await admin.PostAsJsonAsync(
                $"/api/organizations/{org.Id}/datasets", new CreateDatasetRequest("Loans", null, null)))
            .Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);

        var response = await viewer.PostAsync(
            $"/api/organizations/{org.Id}/datasets/{dataset!.Id}/imports", BuildUploadContent(SampleCsv));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
