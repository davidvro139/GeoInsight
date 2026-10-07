using GeoInsight.Application.Common;
using GeoInsight.Application.Geocoding;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Geocoding;

public class GeocodeJobService(
    GeoInsightDbContext dbContext, IDatasetAccessControl accessControl, IBackgroundJobClient backgroundJobClient, IAuditService auditService)
    : IGeocodeJobService
{
    public async Task<ServiceResult<GeocodeJobDto>> CreateAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<GeocodeJobDto>.Failure(accessError.Type, accessError.Message);
        }

        var hasWork = await dbContext.DatasetRecords.AnyAsync(
            r => r.DatasetId == datasetId && (r.GeocodeStatus == "pending" || r.GeocodeStatus == "processing"), cancellationToken);
        if (!hasWork)
        {
            return ServiceResult<GeocodeJobDto>.Failure(
                ServiceErrorType.Validation, "Nothing to geocode — every record already has a status other than pending.");
        }

        var job = new GeocodeJob
        {
            DatasetId = datasetId,
            Status = GeocodeJobStatus.Queued,
            CreatedByUserId = requestingUserId,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.GeocodeJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        var hangfireJobId = backgroundJobClient.Enqueue<IGeocodeJobRunner>(r => r.RunAsync(job.Id, JobCancellationToken.Null));
        job.HangfireJobId = hangfireJobId;
        auditService.Log(organizationId, requestingUserId, "geocoding.start", "geocode_job", job.Id.ToString(), new { datasetId });
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<GeocodeJobDto>.Success(ToDto(job));
    }

    public async Task<ServiceResult<GeocodeJobDto>> GetAsync(
        long organizationId, long datasetId, long jobId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<GeocodeJobDto>.Failure(accessError.Type, accessError.Message);
        }

        var job = await dbContext.GeocodeJobs.SingleOrDefaultAsync(
            j => j.Id == jobId && j.DatasetId == datasetId, cancellationToken);

        return job is null
            ? ServiceResult<GeocodeJobDto>.Failure(ServiceErrorType.NotFound, "Geocode job not found.")
            : ServiceResult<GeocodeJobDto>.Success(ToDto(job));
    }

    public async Task<ServiceResult<IReadOnlyList<GeocodeJobDto>>> GetAllAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<GeocodeJobDto>>.Failure(accessError.Type, accessError.Message);
        }

        var jobs = await dbContext.GeocodeJobs
            .Where(j => j.DatasetId == datasetId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<GeocodeJobDto>>.Success(jobs.Select(ToDto).ToList());
    }

    public async Task<ServiceResult<bool>> CancelAsync(
        long organizationId, long datasetId, long jobId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<bool>.Failure(accessError.Type, accessError.Message);
        }

        var job = await dbContext.GeocodeJobs.SingleOrDefaultAsync(
            j => j.Id == jobId && j.DatasetId == datasetId, cancellationToken);
        if (job is null)
        {
            return ServiceResult<bool>.Failure(ServiceErrorType.NotFound, "Geocode job not found.");
        }

        if (job.Status is GeocodeJobStatus.Queued or GeocodeJobStatus.Running)
        {
            if (job.HangfireJobId is not null)
            {
                // Removes it from the queue if it hasn't started, or signals an
                // already-running job cooperatively via IJobCancellationToken —
                // either way, set the DB status directly since a not-yet-started
                // job's RunAsync body will now never execute at all.
                backgroundJobClient.Delete(job.HangfireJobId);
            }

            job.Status = GeocodeJobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult<bool>.Success(true);
    }

    private static GeocodeJobDto ToDto(GeocodeJob job) => new(
        job.Id, job.Status.ToString(), job.TotalItems, job.CompletedItems, job.SuccessfulItems, job.FailedItems,
        job.StartedAt, job.CompletedAt, job.ErrorMessage, job.CreatedAt);
}
