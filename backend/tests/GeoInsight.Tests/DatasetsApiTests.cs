using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Datasets;
using GeoInsight.Application.Organizations;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Tests;

/// <summary>
/// Same reasoning as OrganizationsApiTests: through the real HTTP/JWT/JSON
/// pipeline, not calling DatasetService directly, so bugs at that boundary
/// (claim mapping, enum/string serialization) can't hide.
/// </summary>
public class DatasetsApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<(HttpClient Client, long UserId)> CreateAuthenticatedClientAsync(string email, string displayName)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123", displayName));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return (client, auth.UserId);
    }

    [Fact]
    public async Task Administrator_can_create_and_fetch_a_dataset()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("ds-admin@example.com", "Admin");
        var org = await (await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/datasets", new CreateDatasetRequest("2026 Mortgage Loans", "Demo data", "loan"));

        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);
        Assert.Equal("loan", created!.DatasetType);

        var getResponse = await client.GetAsync($"/api/organizations/{org.Id}/datasets/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task A_viewer_gets_403_creating_a_dataset()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync("ds-owner@example.com", "Owner");
        var (viewerClient, _) = await CreateAuthenticatedClientAsync("ds-viewer@example.com", "Viewer");

        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        await admin.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/members",
            new AddMemberRequest("ds-viewer@example.com", OrganizationRole.Viewer),
            JsonOptions);

        var response = await viewerClient.PostAsJsonAsync(
            $"/api/organizations/{org.Id}/datasets", new CreateDatasetRequest("Complaints", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_dataset_from_another_organization_is_404_not_403()
    {
        var (ownerA, _) = await CreateAuthenticatedClientAsync("ds-org-a@example.com", "Owner A");
        var (ownerB, _) = await CreateAuthenticatedClientAsync("ds-org-b@example.com", "Owner B");

        var orgA = await (await ownerA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Org A")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        var orgB = await (await ownerB.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Org B")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        var dataset = await (await ownerA.PostAsJsonAsync(
                $"/api/organizations/{orgA!.Id}/datasets", new CreateDatasetRequest("Org A Data", null, null)))
            .Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);

        var response = await ownerB.GetAsync($"/api/organizations/{orgB!.Id}/datasets/{dataset!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
