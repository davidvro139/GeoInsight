using GeoInsight.Application.Common;
using GeoInsight.Application.Geocoding;
using GeoInsight.Application.Review;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Datasets;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using GeoInsight.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

public class RecordReviewServiceTests
{
    private sealed class FakeGeocodingService(Func<GeocodeQuery, GeocodeResult> respond) : IGeocodingService
    {
        public Task<GeocodeResult> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(respond(query));
    }

    private sealed class Harness
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required RecordReviewService Service { get; init; }

        public static Harness Create(Func<GeocodeQuery, GeocodeResult>? geocodeResponse = null)
        {
            var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new GeoInsightDbContext(options);
            var orgAccessControl = new OrganizationAccessControl(dbContext);
            var datasetAccessControl = new DatasetAccessControl(dbContext, orgAccessControl);
            var geocoder = new FakeGeocodingService(geocodeResponse ?? (_ => GeocodeResult.NoMatch()));
            var auditService = new AuditService(dbContext);

            return new Harness
            {
                DbContext = dbContext,
                Service = new RecordReviewService(dbContext, datasetAccessControl, geocoder, auditService),
            };
        }

        public async Task<ApplicationUser> AddUserAsync(string email)
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Test User" };
            DbContext.Users.Add(user);
            await DbContext.SaveChangesAsync();
            return user;
        }

        public async Task<(Organization Org, Dataset Dataset)> AddOrgAndDatasetAsync(ApplicationUser user, OrganizationRole role)
        {
            var org = new Organization { Name = "Acme", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            org.Members.Add(new OrganizationUser { UserId = user.Id, Role = role, CreatedAt = DateTime.UtcNow });
            DbContext.Organizations.Add(org);
            await DbContext.SaveChangesAsync();

            var dataset = new Dataset
            {
                OrganizationId = org.Id, Name = "Loans", CreatedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            };
            DbContext.Datasets.Add(dataset);
            await DbContext.SaveChangesAsync();

            return (org, dataset);
        }

        public async Task<DatasetRecord> AddFailedRecordAsync(long datasetId)
        {
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
            DbContext.DatasetRecords.Add(record);
            await DbContext.SaveChangesAsync();
            return record;
        }

        public async Task<DatasetRecord> AddAmbiguousRecordAsync(long datasetId, IReadOnlyList<GeocodeCandidate> candidates)
        {
            var record = new DatasetRecord
            {
                DatasetId = datasetId,
                AddressLine1 = "100 Main St",
                City = "Portland",
                State = "OR",
                GeocodeStatus = "ambiguous",
                GeocodeCandidates = System.Text.Json.JsonSerializer.Serialize(candidates),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            DbContext.DatasetRecords.Add(record);
            await DbContext.SaveChangesAsync();
            return record;
        }
    }

    private static readonly GeocodeCandidate[] TwoCandidates =
    [
        new("100 SW MAIN ST, PORTLAND, OR", 45.515048m, -122.675175m, "41", "051", "010602", "2", "2067", "41051010602"),
        new("100 SE MAIN ST, PORTLAND, OR", 45.513621m, -122.664737m, "41", "051", "001101", "1", "1099", "41051001101"),
    ];

    [Fact]
    public async Task Queue_lists_only_failed_and_ambiguous_undismissed_records()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        await h.AddFailedRecordAsync(dataset.Id);
        await h.AddAmbiguousRecordAsync(dataset.Id, TwoCandidates);
        h.DbContext.DatasetRecords.Add(new DatasetRecord
        {
            DatasetId = dataset.Id, AddressLine1 = "1 Matched Way", City = "Ogden", State = "UT",
            GeocodeStatus = "matched", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await h.DbContext.SaveChangesAsync();

        var result = await h.Service.GetQueueAsync(org.Id, dataset.Id, user.Id, 1, 50, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    [Fact]
    public async Task A_viewer_cannot_accept_a_candidate()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("viewer@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Viewer);
        var record = await h.AddAmbiguousRecordAsync(dataset.Id, TwoCandidates);

        var result = await h.Service.AcceptCandidateAsync(
            org.Id, dataset.Id, record.Id, new AcceptCandidateRequest(0), user.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task Accepting_a_candidate_applies_its_coordinates_and_marks_manually_resolved()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        var record = await h.AddAmbiguousRecordAsync(dataset.Id, TwoCandidates);

        var result = await h.Service.AcceptCandidateAsync(
            org.Id, dataset.Id, record.Id, new AcceptCandidateRequest(1), user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("manually_resolved", result.Value!.GeocodeStatus);
        Assert.Equal("candidate_selected", result.Value.MatchQuality);
        Assert.Equal(45.513621m, result.Value.Latitude);
        Assert.Empty(result.Value.Candidates);

        var auditEvent = await h.DbContext.AuditEvents.SingleAsync();
        Assert.Equal("record.accept_candidate", auditEvent.Action);
        Assert.Equal(org.Id, auditEvent.OrganizationId);
        Assert.Equal(user.Id, auditEvent.UserId);
    }

    [Fact]
    public async Task An_out_of_range_candidate_index_is_a_validation_error()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        var record = await h.AddAmbiguousRecordAsync(dataset.Id, TwoCandidates);

        var result = await h.Service.AcceptCandidateAsync(
            org.Id, dataset.Id, record.Id, new AcceptCandidateRequest(5), user.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Correcting_an_address_that_now_matches_resolves_it()
    {
        var h = Harness.Create(_ => new GeocodeResult(
            true, false, "1 FIXED ST, OGDEN, UT", 41.22m, -111.97m, "49", "057", "001100", "1", "1001", "49057001100", null));
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        var record = await h.AddFailedRecordAsync(dataset.Id);

        var request = new CorrectAddressRequest("1 Fixed St", null, "Ogden", "UT", "84401");
        var result = await h.Service.CorrectAddressAsync(org.Id, dataset.Id, record.Id, request, user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("manually_resolved", result.Value!.GeocodeStatus);
        Assert.Equal("1 Fixed St", result.Value.AddressLine1);
        Assert.Equal(41.22m, result.Value.Latitude);

        var auditEvent = await h.DbContext.AuditEvents.SingleAsync();
        Assert.Equal("record.correct_address", auditEvent.Action);
    }

    [Fact]
    public async Task Correcting_an_address_that_is_still_unmatched_stays_failed()
    {
        var h = Harness.Create(_ => GeocodeResult.NoMatch());
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        var record = await h.AddFailedRecordAsync(dataset.Id);

        var request = new CorrectAddressRequest("Still Bad Rd", null, "Ogden", "UT", null);
        var result = await h.Service.CorrectAddressAsync(org.Id, dataset.Id, record.Id, request, user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("failed", result.Value!.GeocodeStatus);
        Assert.Equal("Still Bad Rd", result.Value.AddressLine1); // address is still updated even though it didn't match
    }

    [Fact]
    public async Task Setting_coordinates_directly_marks_manually_resolved_with_manual_source()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        var record = await h.AddFailedRecordAsync(dataset.Id);

        var result = await h.Service.SetCoordinatesAsync(
            org.Id, dataset.Id, record.Id, new SetCoordinatesRequest(41.5m, -111.5m), user.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("manually_resolved", result.Value!.GeocodeStatus);
        Assert.Equal("manual", result.Value.MatchQuality);
        Assert.Equal("manual", result.Value.GeocoderSource);
        Assert.Equal(41.5m, result.Value.Latitude);
    }

    [Fact]
    public async Task Dismissing_a_record_removes_it_from_the_queue_without_changing_its_status()
    {
        var h = Harness.Create();
        var user = await h.AddUserAsync("analyst@example.com");
        var (org, dataset) = await h.AddOrgAndDatasetAsync(user, OrganizationRole.Analyst);
        var record = await h.AddFailedRecordAsync(dataset.Id);

        var result = await h.Service.DismissAsync(
            org.Id, dataset.Id, record.Id, new DismissRequest("Address does not exist"), user.Id, CancellationToken.None);
        var queue = await h.Service.GetQueueAsync(org.Id, dataset.Id, user.Id, 1, 50, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("failed", result.Value!.GeocodeStatus); // status is untouched, just removed from the active queue
        Assert.Equal(0, queue.Value!.TotalCount);
    }

    [Fact]
    public async Task A_record_from_another_organization_is_not_found()
    {
        var h = Harness.Create();
        var userA = await h.AddUserAsync("a@example.com");
        var userB = await h.AddUserAsync("b@example.com");
        var (orgA, datasetA) = await h.AddOrgAndDatasetAsync(userA, OrganizationRole.Administrator);
        var (orgB, _) = await h.AddOrgAndDatasetAsync(userB, OrganizationRole.Administrator);
        var record = await h.AddFailedRecordAsync(datasetA.Id);

        var result = await h.Service.GetAsync(orgB.Id, datasetA.Id, record.Id, userB.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }
}
