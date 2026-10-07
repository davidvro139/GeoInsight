using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Organizations;
using GeoInsight.Application.Tracts;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace GeoInsight.Tests;

/// <summary>
/// HTTP-level permission/listing flow for the standalone, no-dataset Census
/// data import path. No Hangfire server runs jobs inside this test process
/// (see GeocodeJobsApiTests' note), so this can't assert an import actually
/// completes — that's TractImportRunnerTests' job.
/// </summary>
public class CensusDataApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
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
            "/api/auth/register", new RegisterRequest(email, "Password123", "Census Data Tester"));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    [Fact]
    public async Task An_administrator_can_trigger_a_standalone_county_import()
    {
        var admin = await CreateAuthenticatedClientAsync("census-data-admin@example.com");
        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        var response = await admin.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/census-data/import", new RequestCountyImportRequest("49", "057"), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_non_administrator_gets_403_requesting_a_standalone_import()
    {
        var admin = await CreateAuthenticatedClientAsync("census-data-owner@example.com");
        var viewer = await CreateAuthenticatedClientAsync("census-data-viewer@example.com");
        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        await admin.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/members",
            new AddMemberRequest("census-data-viewer@example.com", OrganizationRole.Viewer),
            JsonOptions);

        var response = await viewer.PostAsJsonAsync(
            $"/api/organizations/{org.Id}/census-data/import", new RequestCountyImportRequest("49", "057"), JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Imported_counties_reflects_a_tract_seeded_directly_in_the_database()
    {
        var client = await CreateAuthenticatedClientAsync("census-data-list@example.com");
        var org = await (await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        var factoryInstance = new GeometryFactory(new PrecisionModel(), 4326);
        var square = factoryInstance.CreatePolygon([
            new Coordinate(-112, 41), new Coordinate(-111, 41),
            new Coordinate(-111, 42), new Coordinate(-112, 42), new Coordinate(-112, 41),
        ]);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<GeoInsightDbContext>();
            dbContext.CensusTracts.Add(new CensusTract
            {
                Geoid = "49099000100", StateFips = "49", CountyFips = "099", TractCode = "000100",
                TractName = "Census Tract 1", Boundary = factoryInstance.CreateMultiPolygon([square]),
            });
            dbContext.SaveChanges();
        }

        var counties = await (await client.GetAsync($"/api/organizations/{org!.Id}/census-data/imported-counties"))
            .Content.ReadFromJsonAsync<List<ImportedCountyDto>>(JsonOptions);

        Assert.Contains(counties!, c => c.StateFips == "49" && c.CountyFips == "099" && c.TractCount == 1);
    }
}
