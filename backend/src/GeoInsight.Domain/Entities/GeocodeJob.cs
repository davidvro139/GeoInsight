namespace GeoInsight.Domain.Entities;

public enum GeocodeJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public class GeocodeJob
{
    public long Id { get; set; }

    public long DatasetId { get; set; }

    public GeocodeJobStatus Status { get; set; } = GeocodeJobStatus.Queued;

    public int TotalItems { get; set; }

    public int CompletedItems { get; set; }

    public int SuccessfulItems { get; set; }

    public int FailedItems { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Lets the Cancel endpoint call BackgroundJobClient.Delete(...) — Hangfire
    /// signals an already-running job cooperatively through the same
    /// IJobCancellationToken the runner checks each iteration.
    /// </summary>
    public string? HangfireJobId { get; set; }

    public long CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
