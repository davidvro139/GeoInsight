using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Common;
using GeoInsight.Application.Datasets;
using GeoInsight.Application.Geocoding;
using GeoInsight.Application.Organizations;
using GeoInsight.Application.Review;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GeoInsight.Tests;

/// <summary>
/// HTTP-level review-queue flow. Failed/ambiguous seed records are written
/// directly via the DbContext (no Hangfire server runs jobs inside the test
/// process, per GeocodeJobsApiTests' note) rather than produced by an actual
/// geocode job. correct-address is exercised through TestApiFactory's
/// swappable fake IGeocodingService, never the real Census API.
/// </summary>
public class RecordReviewApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
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
            "/api/auth/register", new RegisterRequest(email, "Password123", "Review Tester"));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private async Task<(long OrgId, long DatasetId)> SeedDatasetAsync(HttpClient client)
    {
        var org = await (await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        var dataset = await (await client.PostAsJsonAsync(
                $"/api/organizations/{org!.Id}/datasets", new CreateDatasetRequest("Loans", null, null)))
            .Content.ReadFromJsonAsync<DatasetDetailDto>(JsonOptions);

        return (org.Id, dataset!.Id);
    }

    private long SeedFailedRecord(long datasetId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GeoInsightDbContext>();
        var record = new DatasetRecord
        {
            DatasetId = datasetId,
            AddressLine1 = "999 Nowhere Rd",
            City = "Ogden",
            State = "UT",
            GeocodeStatus = "failed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        dbContext.DatasetRecords.Add(record);
        dbContext.SaveChanges();
        return record.Id;
    }

    private long SeedAmbiguousRecord(long datasetId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GeoInsightDbContext>();
        var candidates = new List<GeocodeCandidate>
        {
            new("100 SW MAIN ST, PORTLAND, OR", 45.515048m, -122.675175m, "41", "051", "010602", "2", "2067", "41051010602"),
            new("100 SE MAIN ST, PORTLAND, OR", 45.513621m, -122.664737m, "41", "051", "001101", "1", "1099", "41051001101"),
        };
        var record = new DatasetRecord
        {
            DatasetId = datasetId,
            AddressLine1 = "100 Main St",
            City = "Portland",
            State = "OR",
            GeocodeStatus = "ambiguous",
            GeocodeCandidates = JsonSerializer.Serialize(candidates),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        dbContext.DatasetRecords.Add(record);
        dbContext.SaveChanges();
        return record.Id;
    }

    [Fact]
    public async Task The_queue_lists_seeded_failed_and_ambiguous_records()
    {
        var client = await CreateAuthenticatedClientAsync("review-queue@example.com");
        var (orgId, datasetId) = await SeedDatasetAsync(client);
        SeedFailedRecord(datasetId);
        SeedAmbiguousRecord(datasetId);

        var response = await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/records/review");
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PagedResult<ReviewRecordDto>>(JsonOptions);

        Assert.Equal(2, page!.TotalCount);
    }

    [Fact]
    public async Task Accepting_a_candidate_resolves_the_record_through_the_real_pipeline()
    {
        var client = await CreateAuthenticatedClientAsync("review-accept@example.com");
        var (orgId, datasetId) = await SeedDatasetAsync(client);
        var recordId = SeedAmbiguousRecord(datasetId);

        var response = await client.PostAsJsonAsync(
            $"/api/organizations/{orgId}/datasets/{datasetId}/records/review/{recordId}/accept-candidate",
            new AcceptCandidateRequest(0), JsonOptions);
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<ReviewRecordDto>(JsonOptions);

        Assert.Equal("manually_resolved", updated!.GeocodeStatus);
        Assert.Equal(45.515048m, updated.Latitude);
    }

    [Fact]
    public async Task Correcting_an_address_uses_the_fake_geocoder_and_resolves_on_a_match()
    {
        factory.GeocodeResponse = _ => new GeocodeResult(
            true, false, "1 FIXED ST, OGDEN, UT", 41.22m, -111.97m, "49", "057", "001100", "1", "1001", "49057001100", null);

        var client = await CreateAuthenticatedClientAsync("review-correct@example.com");
        var (orgId, datasetId) = await SeedDatasetAsync(client);
        var recordId = SeedFailedRecord(datasetId);

        var response = await client.PostAsJsonAsync(
            $"/api/organizations/{orgId}/datasets/{datasetId}/records/review/{recordId}/correct-address",
            new CorrectAddressRequest("1 Fixed St", null, "Ogden", "UT", "84401"), JsonOptions);
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<ReviewRecordDto>(JsonOptions);

        Assert.Equal("manually_resolved", updated!.GeocodeStatus);
        Assert.Equal(41.22m, updated.Latitude);

        factory.GeocodeResponse = _ => GeocodeResult.NoMatch(); // reset for other tests sharing the factory
    }

    [Fact]
    public async Task Setting_coordinates_and_then_dismissing_another_record_both_work()
    {
        var client = await CreateAuthenticatedClientAsync("review-manual@example.com");
        var (orgId, datasetId) = await SeedDatasetAsync(client);
        var coordsRecordId = SeedFailedRecord(datasetId);
        var dismissRecordId = SeedFailedRecord(datasetId);

        var coordsResponse = await client.PostAsJsonAsync(
            $"/api/organizations/{orgId}/datasets/{datasetId}/records/review/{coordsRecordId}/coordinates",
            new SetCoordinatesRequest(41.5m, -111.5m), JsonOptions);
        coordsResponse.EnsureSuccessStatusCode();
        var coordsUpdated = await coordsResponse.Content.ReadFromJsonAsync<ReviewRecordDto>(JsonOptions);
        Assert.Equal("manually_resolved", coordsUpdated!.GeocodeStatus);
        Assert.Equal("manual", coordsUpdated.GeocoderSource);

        var dismissResponse = await client.PostAsJsonAsync(
            $"/api/organizations/{orgId}/datasets/{datasetId}/records/review/{dismissRecordId}/dismiss",
            new DismissRequest("No such address"), JsonOptions);
        dismissResponse.EnsureSuccessStatusCode();

        var queue = await (await client.GetAsync($"/api/organizations/{orgId}/datasets/{datasetId}/records/review"))
            .Content.ReadFromJsonAsync<PagedResult<ReviewRecordDto>>(JsonOptions);
        Assert.Equal(0, queue!.TotalCount); // both resolved: one matched, one dismissed
    }

    [Fact]
    public async Task A_viewer_gets_403_correcting_an_address()
    {
        var owner = await CreateAuthenticatedClientAsync("review-owner@example.com");
        var viewer = await CreateAuthenticatedClientAsync("review-viewer@example.com");
        var (orgId, datasetId) = await SeedDatasetAsync(owner);
        var recordId = SeedFailedRecord(datasetId);
        await owner.PostAsJsonAsync(
            $"/api/organizations/{orgId}/members",
            new AddMemberRequest("review-viewer@example.com", OrganizationRole.Viewer),
            JsonOptions);

        var response = await viewer.PostAsJsonAsync(
            $"/api/organizations/{orgId}/datasets/{datasetId}/records/review/{recordId}/correct-address",
            new CorrectAddressRequest("1 Fixed St", null, "Ogden", "UT", null), JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
