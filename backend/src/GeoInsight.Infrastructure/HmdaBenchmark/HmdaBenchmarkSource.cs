using System.Text.Json;
using GeoInsight.Application.HmdaBenchmark;
using Microsoft.Extensions.Logging;

namespace GeoInsight.Infrastructure.HmdaBenchmark;

/// <summary>
/// Calls the real CFPB HMDA Data Browser API (confirmed live, free, keyless:
/// ffiec.cfpb.gov/v2/data-browser-api/view/aggregations). County-level only —
/// a tract-level filter doesn't exist on this endpoint (confirmed empirically:
/// a "tracts" param is silently ignored), and combining multiple counties in
/// one call sums them together rather than breaking them out, so callers must
/// request one county at a time. actions_taken=1 ("originated") is the "real
/// market" definition used here — every other HMDA filer's completed loans in
/// that county, not applications/denials/withdrawals. The API's error bodies
/// are inconsistent (JSON object, bare string, or plain text depending on the
/// failure), so any non-200 or unexpected shape is treated as "no data,"
/// never thrown — one flaky county lookup must not fail the whole benchmark.
/// </summary>
public class HmdaBenchmarkSource(HttpClient httpClient, ILogger<HmdaBenchmarkSource> logger) : IHmdaBenchmarkSource
{
    public async Task<HmdaCountyAggregate?> GetOriginatedLoanAggregateAsync(
        string countyFips, int year, CancellationToken cancellationToken)
    {
        var requestUri = $"v2/data-browser-api/view/aggregations?years={year}&counties={countyFips}&actions_taken=1";

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "HMDA benchmark lookup for county {County} year {Year} returned {Status}",
                    countyFips, year, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("aggregations", out var aggregations) ||
                aggregations.ValueKind != JsonValueKind.Array || aggregations.GetArrayLength() == 0)
            {
                return null;
            }

            var first = aggregations[0];
            if (!first.TryGetProperty("count", out var countElement) || !first.TryGetProperty("sum", out var sumElement))
            {
                return null;
            }

            // Confirmed live: this aggregation endpoint's "sum" is already in real
            // dollars, not thousands — e.g. Weber County, UT 2023: count=7806,
            // sum=1.95266E9, giving a ~$250k average loan, a plausible mortgage
            // size. (Raw HMDA LAR records report loan_amount in thousands, but
            // this pre-aggregated endpoint does not.)
            return new HmdaCountyAggregate(countElement.GetInt64(), sumElement.GetDecimal());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "HMDA benchmark lookup failed for county {County} year {Year}", countyFips, year);
            return null;
        }
    }
}
