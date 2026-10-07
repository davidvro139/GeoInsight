using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Datasets;
using GeoInsight.Application.Organizations;
using GeoInsight.Application.Tracts;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace GeoInsight.Tests;

/// <summary>
/// HTTP-level status/import/list flow. No Hangfire server runs jobs inside
/// this test process (see GeocodeJobsApiTests' note), so this can't assert an
/// import actually completes — that's TractImportRunnerTests' job. Records
/// with Census FIPS are seeded directly via the DbContext.
/// </summary>
public class CensusTractsApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
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
            "/api/auth/register", new RegisterRequest(email, "Password123", "Tract Tester"));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    // census_tracts is global reference data shared by every test in this class
    // (one InMemory database per IClassFixture<TestApiFactory>), so each test
    // uses its own county FIPS to avoid one test's seeded tract leaking into
    // another test's dataset that happens to reference the same county.
    private async Task<(long OrgId, long DatasetId)> SeedDatasetWithAGeocodedRecordAsync(HttpClient client, string countyFips)
    {
        var org = await (await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        var dataset = await (await client.PostAsJsonAsync(
                $"/api/organizations/{org!.Id}/datasets", new CreateDatasetRequest("Loans", null, null)))
            .Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GeoInsightDbContext>();
        dbContext.DatasetRecords.Add(new DatasetRecord
        {
            DatasetId = dataset!.Id, AddressLine1 = "100 Main St", City = "Ogden", State = "UT",
            GeocodeStatus = "matched", CensusStateFips = "49", CensusCountyFips = countyFips,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        dbContext.SaveChanges();

        return (org.Id, dataset.Id);
    }

    [Fact]
    public async Task Status_shows_the_needed_county_before_any_import()
    {
        var client = await CreateAuthenticatedClientAsync("tract-status@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAGeocodedRecordAsync(client, "001");

        var response = await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/tracts/status");
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<TractImportStatusDto>(JsonOptions);

        Assert.False(status!.AllImported);
        Assert.Single(status.NeededCounties);
        Assert.Equal("49", status.NeededCounties[0].StateFips);
    }

    [Fact]
    public async Task A_csv_coordinate_record_with_no_known_county_reports_unassigned_coordinates()
    {
        var client = await CreateAuthenticatedClientAsync("tract-csv-coords@example.com");
        var org = await (await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        var dataset = await (await client.PostAsJsonAsync(
                $"/api/organizations/{org!.Id}/datasets", new CreateDatasetRequest("CSV Loans", null, null)))
            .Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<GeoInsightDbContext>();
            dbContext.DatasetRecords.Add(new DatasetRecord
            {
                DatasetId = dataset!.Id, AddressLine1 = "1 CSV Way", City = "Ogden", State = "UT",
                GeocodeStatus = "matched", GeocoderSource = "csv_import",
                Latitude = 41.2m, Longitude = -111.9m, // has coordinates, no CensusStateFips/CountyFips at all
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            dbContext.SaveChanges();
        }

        var status = await (await client.GetAsync($"/api/organizations/{org.Id}/datasets/{dataset!.Id}/tracts/status"))
            .Content.ReadFromJsonAsync<TractImportStatusDto>(JsonOptions);

        // Nothing in NeededCounties (there's no geocoder-derived county to know
        // about yet) but the dataset still isn't "done" — this is exactly the
        // gap BackfillOnlyAsync exists to close.
        Assert.Empty(status!.NeededCounties);
        Assert.True(status.HasUnassignedCoordinates);
        Assert.False(status.AllImported);

        var importResponse = await client.PostAsync($"/api/organizations/{org.Id}/datasets/{dataset.Id}/tracts/import", null);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
    }

    [Fact]
    public async Task Requesting_an_import_succeeds_through_the_real_pipeline()
    {
        var client = await CreateAuthenticatedClientAsync("tract-import@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAGeocodedRecordAsync(client, "003");

        var response = await client.PostAsync($"/api/organizations/{orgId}/datasets/{datasetId}/tracts/import", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_viewer_gets_403_requesting_an_import()
    {
        var owner = await CreateAuthenticatedClientAsync("tract-owner@example.com");
        var viewer = await CreateAuthenticatedClientAsync("tract-viewer@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAGeocodedRecordAsync(owner, "005");
        await owner.PostAsJsonAsync(
            $"/api/organizations/{orgId}/members",
            new AddMemberRequest("tract-viewer@example.com", OrganizationRole.Viewer),
            JsonOptions);

        var response = await viewer.PostAsync($"/api/organizations/{orgId}/datasets/{datasetId}/tracts/import", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Boundaries_list_is_empty_before_any_tracts_are_imported()
    {
        var client = await CreateAuthenticatedClientAsync("tract-boundaries@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAGeocodedRecordAsync(client, "007");

        var response = await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/tracts");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(0, json!.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task An_already_imported_boundary_serializes_as_real_geojson()
    {
        var client = await CreateAuthenticatedClientAsync("tract-geojson@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAGeocodedRecordAsync(client, "009");

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
                Geoid = "49009000100", StateFips = "49", CountyFips = "009", TractCode = "000100",
                TractName = "Census Tract 1", Boundary = factoryInstance.CreateMultiPolygon([square]),
            });
            dbContext.SaveChanges();
        }

        var response = await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/tracts");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();

        var boundary = json!.RootElement[0].GetProperty("boundary");
        Assert.Equal("MultiPolygon", boundary.GetProperty("type").GetString());
        Assert.Equal("49009000100", json.RootElement[0].GetProperty("geoid").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement[0].GetProperty("demographics").ValueKind); // none seeded for this tract
    }

    [Fact]
    public async Task A_tract_with_demographics_returns_them_alongside_its_boundary()
    {
        var client = await CreateAuthenticatedClientAsync("tract-demographics@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAGeocodedRecordAsync(client, "011");

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
                Geoid = "49011000100", StateFips = "49", CountyFips = "011", TractCode = "000100",
                TractName = "Census Tract 1", Boundary = factoryInstance.CreateMultiPolygon([square]),
            });
            dbContext.CensusDemographics.Add(new CensusDemographic
            {
                CensusGeoid = "49011000100", SurveyYear = 2022, Population = 4200,
                MedianHouseholdIncome = 71_500m, PovertyRate = 9.2m, MedianAge = 36.5m,
                OwnerOccupiedPercent = 62m, RenterOccupiedPercent = 38m, MinorityPopulationPercent = 22m,
            });
            dbContext.SaveChanges();
        }

        var response = await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/tracts");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();

        var demographics = json!.RootElement[0].GetProperty("demographics");
        Assert.Equal(4200, demographics.GetProperty("population").GetInt32());
        Assert.Equal(71_500m, demographics.GetProperty("medianHouseholdIncome").GetDecimal());
        Assert.Equal(2022, demographics.GetProperty("surveyYear").GetInt32());
        Assert.True(demographics.GetProperty("isEstimated").GetBoolean()); // RawData was never set on this seeded row
    }

    [Fact]
    public async Task Refresh_demographics_requires_a_configured_api_key()
    {
        var client = await CreateAuthenticatedClientAsync("tract-refresh-no-key@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAGeocodedRecordAsync(client, "013");

        var response = await client.PostAsync($"/api/organizations/{orgId}/datasets/{datasetId}/tracts/refresh-demographics", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_demographics_succeeds_once_an_api_key_is_configured()
    {
        var client = await CreateAuthenticatedClientAsync("tract-refresh-with-key@example.com");
        var (orgId, datasetId) = await SeedDatasetWithAGeocodedRecordAsync(client, "015");

        var keyResponse = await client.PutAsJsonAsync(
            $"/api/organizations/{orgId}/census-api-key", new SetCensusApiKeyRequest("a-real-key"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, keyResponse.StatusCode);

        var org = await (await client.GetAsync($"/api/organizations/{orgId}"))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        Assert.True(org!.HasCensusApiKey);

        var response = await client.PostAsync($"/api/organizations/{orgId}/datasets/{datasetId}/tracts/refresh-demographics", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
