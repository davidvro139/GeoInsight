using GeoInsight.Application.Common;
using GeoInsight.Application.Geocoding;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoInsight.Infrastructure.Geocoding;

/// <summary>
/// Only ever touches records where GeocodeStatus is "pending" or "processing"
/// — this one query is what makes "avoid duplicate processing" (skips
/// anything already matched, including csv_import coordinates from Phase 4)
/// and "resume incomplete jobs" (re-picks-up anything abandoned mid-
/// "processing" by a crashed worker) both fall out for free.
/// </summary>
public class GeocodeJobRunner(GeoInsightDbContext dbContext, IGeocodingService geocodingService, IAuditService auditService, ILogger<GeocodeJobRunner> logger)
    : IGeocodeJobRunner
{
    public async Task RunAsync(long jobId, IJobCancellationToken cancellationToken)
    {
        var job = await dbContext.GeocodeJobs.FindAsync(jobId);
        if (job is null)
        {
            logger.LogWarning("GeocodeJob {JobId} not found; skipping", jobId);
            return;
        }

        // "Completion" (below) has no request-scoped organizationId/user to
        // hand IAuditService the way "start" (GeocodeJobService.CreateAsync)
        // does — this runs as a Hangfire background job — so it's looked up
        // once here and the event is attributed to whoever originally queued it.
        var organizationId = await dbContext.Datasets
            .Where(d => d.Id == job.DatasetId)
            .Select(d => d.OrganizationId)
            .SingleAsync();

        var records = await dbContext.DatasetRecords
            .Where(r => r.DatasetId == job.DatasetId && (r.GeocodeStatus == "pending" || r.GeocodeStatus == "processing"))
            .ToListAsync();

        job.Status = GeocodeJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.TotalItems = records.Count;
        await dbContext.SaveChangesAsync();

        try
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessRecordAsync(record, cancellationToken);

                job.CompletedItems++;
                if (record.GeocodeStatus == "matched")
                {
                    job.SuccessfulItems++;
                }
                else
                {
                    job.FailedItems++;
                }

                await dbContext.SaveChangesAsync();
            }

            job.Status = GeocodeJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            LogCompletion(organizationId, job);
            await dbContext.SaveChangesAsync();
        }
        catch (JobAbortedException)
        {
            job.Status = GeocodeJobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            LogCompletion(organizationId, job);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GeocodeJob {JobId} failed", jobId);
            job.Status = GeocodeJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            LogCompletion(organizationId, job);
            await dbContext.SaveChangesAsync();
            throw; // let Hangfire's automatic retry pick this job back up
        }
    }

    /// <summary>
    /// One action for all three terminal states (Completed/Cancelled/Failed)
    /// — "the job ended", with the actual outcome in the metadata, rather
    /// than three near-identical action strings for the same conceptual event.
    /// </summary>
    private void LogCompletion(long organizationId, GeocodeJob job) =>
        auditService.Log(organizationId, job.CreatedByUserId, "geocoding.complete", "geocode_job", job.Id.ToString(),
            new { datasetId = job.DatasetId, status = job.Status.ToString(), job.TotalItems, job.SuccessfulItems, job.FailedItems });

    private async Task ProcessRecordAsync(DatasetRecord record, IJobCancellationToken cancellationToken)
    {
        record.NormalizedAddress = BuildNormalizedAddress(record);
        record.GeocodeStatus = "processing";
        await dbContext.SaveChangesAsync();

        GeocodeResult result;
        try
        {
            result = await geocodingService.GeocodeAsync(
                new GeocodeQuery(record.AddressLine1, record.AddressLine2, record.City, record.State, record.PostalCode),
                cancellationToken.ShutdownToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Geocoding record {RecordId} failed unexpectedly", record.Id);
            result = GeocodeResult.Error(ex.Message);
        }

        GeocodeResultApplier.Apply(record, result, successStatus: "matched", matchQuality: "exact");
    }

    private static string BuildNormalizedAddress(DatasetRecord record)
    {
        var line = string.IsNullOrWhiteSpace(record.AddressLine2)
            ? record.AddressLine1
            : $"{record.AddressLine1} {record.AddressLine2}";
        return $"{line}, {record.City}, {record.State} {record.PostalCode}".TrimEnd();
    }
}
