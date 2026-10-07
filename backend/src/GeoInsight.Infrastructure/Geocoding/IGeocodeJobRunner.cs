using Hangfire;

namespace GeoInsight.Infrastructure.Geocoding;

/// <summary>
/// The method Hangfire actually invokes. Lives in Infrastructure (not
/// Application, unlike the other service interfaces) because its shape is
/// inherently a Hangfire integration detail — IJobCancellationToken is how
/// Hangfire cooperatively signals an already-running job that
/// BackgroundJobClient.Delete(...) was called on it.
/// </summary>
public interface IGeocodeJobRunner
{
    Task RunAsync(long jobId, IJobCancellationToken cancellationToken);
}
