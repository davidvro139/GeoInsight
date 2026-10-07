using GeoInsight.Application.Geocoding;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Geocoding;
using GeoInsight.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoInsight.Tests;

/// <summary>
/// Uses a fake IGeocodingService (no live Census calls) to exercise the
/// runner's own logic: status mapping, skip-already-matched, counters.
/// </summary>
public class GeocodeJobRunnerTests
{
    private sealed class FakeGeocodingService(Func<GeocodeQuery, GeocodeResult> respond) : IGeocodingService
    {
        public Task<GeocodeResult> GeocodeAsync(GeocodeQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(respond(query));
    }

    private static GeoInsightDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<GeoInsightDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>
    /// Every test below hardcodes DatasetId = 1 on its GeocodeJob/DatasetRecord
    /// rows — this seeds a matching Dataset row so GeocodeJobRunner's new
    /// organizationId lookup (for the "geocoding.complete" audit event) has
    /// something real to find, the same way it would in production.
    /// </summary>
    private static async Task SeedDatasetAsync(GeoInsightDbContext dbContext, long id = 1)
    {
        dbContext.Datasets.Add(new Dataset
        {
            Id = id, OrganizationId = 1, Name = "Test Dataset", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    private static DatasetRecord PendingRecord(long datasetId, string address = "100 Main St") => new()
    {
        DatasetId = datasetId,
        AddressLine1 = address,
        City = "Ogden",
        State = "UT",
        GeocodeStatus = "pending",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task A_single_match_is_marked_matched_with_coordinates_applied()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var job = new GeocodeJob { DatasetId = 1, CreatedAt = DateTime.UtcNow };
        dbContext.GeocodeJobs.Add(job);
        var record = PendingRecord(1);
        dbContext.DatasetRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var geocoder = new FakeGeocodingService(_ => new GeocodeResult(
            true, false, "100 MAIN ST, OGDEN, UT", 41.22m, -111.97m, "49", "057", "001100", "1", "1001", "49057001100", null));
        var runner = new GeocodeJobRunner(dbContext, geocoder, new AuditService(dbContext), NullLogger<GeocodeJobRunner>.Instance);

        await runner.RunAsync(job.Id, new JobCancellationToken(false));

        await dbContext.Entry(record).ReloadAsync();
        Assert.Equal("matched", record.GeocodeStatus);
        Assert.Equal(41.22m, record.Latitude);
        Assert.Equal("census", record.GeocoderSource);
        Assert.NotNull(record.GeocodedAt);
        Assert.Equal(GeocodeJobStatus.Completed, job.Status);
        Assert.Equal(1, job.TotalItems);
        Assert.Equal(1, job.SuccessfulItems);
        Assert.Equal(0, job.FailedItems);

        var auditEvent = Assert.Single(dbContext.AuditEvents);
        Assert.Equal("geocoding.complete", auditEvent.Action);
        Assert.Equal("geocode_job", auditEvent.EntityType);
        Assert.Equal(job.Id.ToString(), auditEvent.EntityId);
        Assert.Contains("\"status\":\"Completed\"", auditEvent.Metadata);
    }

    [Fact]
    public async Task A_job_that_throws_is_marked_failed_and_still_logs_a_geocoding_complete_event()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var job = new GeocodeJob { DatasetId = 1, CreatedByUserId = 42, CreatedAt = DateTime.UtcNow };
        dbContext.GeocodeJobs.Add(job);
        dbContext.DatasetRecords.Add(PendingRecord(1));
        await dbContext.SaveChangesAsync();

        var geocoder = new FakeGeocodingService(_ => GeocodeResult.NoMatch());
        var runner = new GeocodeJobRunner(dbContext, geocoder, new AuditService(dbContext), NullLogger<GeocodeJobRunner>.Instance);

        // An already-cancelled token throws a real OperationCanceledException
        // (not the Hangfire-server-shutdown-only JobAbortedException — that
        // needs real Hangfire server internals a unit test can't construct),
        // which the runner's generic catch block treats as a job failure and
        // rethrows for Hangfire's automatic retry to pick back up.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync(job.Id, new JobCancellationToken(true)));

        Assert.Equal(GeocodeJobStatus.Failed, job.Status);
        var auditEvent = Assert.Single(dbContext.AuditEvents);
        Assert.Equal("geocoding.complete", auditEvent.Action);
        Assert.Equal(42, auditEvent.UserId); // attributed to job.CreatedByUserId — no request-scoped user in a background job
        Assert.Contains("\"status\":\"Failed\"", auditEvent.Metadata);
    }

    [Fact]
    public async Task An_ambiguous_match_does_not_apply_coordinates_but_stores_candidates()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var job = new GeocodeJob { DatasetId = 1, CreatedAt = DateTime.UtcNow };
        dbContext.GeocodeJobs.Add(job);
        var record = PendingRecord(1);
        dbContext.DatasetRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var candidates = new List<GeocodeCandidate>
        {
            new("100 MAIN ST N, OGDEN, UT", 41.22m, -111.97m, "49", "057", "001100", "1", "1001", "49057001100"),
            new("100 MAIN ST S, OGDEN, UT", 41.20m, -111.96m, "49", "057", "001200", "2", "2002", "49057001200"),
        };
        var geocoder = new FakeGeocodingService(_ => GeocodeResult.AmbiguousMatch(candidates));
        var runner = new GeocodeJobRunner(dbContext, geocoder, new AuditService(dbContext), NullLogger<GeocodeJobRunner>.Instance);

        await runner.RunAsync(job.Id, new JobCancellationToken(false));

        await dbContext.Entry(record).ReloadAsync();
        Assert.Equal("ambiguous", record.GeocodeStatus);
        Assert.Null(record.Latitude);
        Assert.NotNull(record.GeocodeCandidates);
        var stored = System.Text.Json.JsonSerializer.Deserialize<List<GeocodeCandidate>>(record.GeocodeCandidates!);
        Assert.Equal(2, stored!.Count);
        Assert.Equal("100 MAIN ST N, OGDEN, UT", stored[0].MatchedAddress);
        Assert.Equal(1, job.FailedItems);
        Assert.Equal(0, job.SuccessfulItems);
    }

    [Fact]
    public async Task A_no_match_result_is_marked_failed()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var job = new GeocodeJob { DatasetId = 1, CreatedAt = DateTime.UtcNow };
        dbContext.GeocodeJobs.Add(job);
        var record = PendingRecord(1);
        dbContext.DatasetRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var geocoder = new FakeGeocodingService(_ => GeocodeResult.NoMatch());
        var runner = new GeocodeJobRunner(dbContext, geocoder, new AuditService(dbContext), NullLogger<GeocodeJobRunner>.Instance);

        await runner.RunAsync(job.Id, new JobCancellationToken(false));

        await dbContext.Entry(record).ReloadAsync();
        Assert.Equal("failed", record.GeocodeStatus);
    }

    [Fact]
    public async Task Already_matched_records_are_skipped()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var job = new GeocodeJob { DatasetId = 1, CreatedAt = DateTime.UtcNow };
        dbContext.GeocodeJobs.Add(job);
        var alreadyMatched = PendingRecord(1, "Already matched");
        alreadyMatched.GeocodeStatus = "matched";
        alreadyMatched.GeocoderSource = "csv_import";
        var pending = PendingRecord(1, "Still pending");
        dbContext.DatasetRecords.AddRange(alreadyMatched, pending);
        await dbContext.SaveChangesAsync();

        var callCount = 0;
        var geocoder = new FakeGeocodingService(_ =>
        {
            callCount++;
            return GeocodeResult.NoMatch();
        });
        var runner = new GeocodeJobRunner(dbContext, geocoder, new AuditService(dbContext), NullLogger<GeocodeJobRunner>.Instance);

        await runner.RunAsync(job.Id, new JobCancellationToken(false));

        Assert.Equal(1, callCount); // only the still-pending record triggers a geocode call
        Assert.Equal(1, job.TotalItems);
        await dbContext.Entry(alreadyMatched).ReloadAsync();
        Assert.Equal("matched", alreadyMatched.GeocodeStatus); // untouched
    }

    [Fact]
    public async Task A_record_left_processing_by_a_crashed_worker_is_picked_up_again()
    {
        await using var dbContext = CreateContext();
        await SeedDatasetAsync(dbContext);
        var job = new GeocodeJob { DatasetId = 1, CreatedAt = DateTime.UtcNow };
        dbContext.GeocodeJobs.Add(job);
        var abandoned = PendingRecord(1, "Abandoned mid-processing");
        abandoned.GeocodeStatus = "processing";
        dbContext.DatasetRecords.Add(abandoned);
        await dbContext.SaveChangesAsync();

        var geocoder = new FakeGeocodingService(_ => new GeocodeResult(
            true, false, "ADDR", 1m, 2m, "49", "057", "001100", "1", "1001", "49057001100", null));
        var runner = new GeocodeJobRunner(dbContext, geocoder, new AuditService(dbContext), NullLogger<GeocodeJobRunner>.Instance);

        await runner.RunAsync(job.Id, new JobCancellationToken(false));

        await dbContext.Entry(abandoned).ReloadAsync();
        Assert.Equal("matched", abandoned.GeocodeStatus);
    }
}
