using System.Text.Json;
using GeoInsight.Application.Geocoding;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Infrastructure.Geocoding;

/// <summary>
/// Applies a GeocodeResult (or a manually-chosen outcome) to a DatasetRecord's
/// geocode fields. Shared by the background GeocodeJobRunner (Phase 5) and the
/// Address Review Queue's manual-correction actions (Phase 6) so the same
/// field mapping isn't duplicated between an automatic and a human-driven path.
/// </summary>
internal static class GeocodeResultApplier
{
    public static void ApplyMatch(
        DatasetRecord record,
        string status,
        string matchQuality,
        string geocoderSource,
        string? matchedAddress,
        decimal latitude,
        decimal longitude,
        string? stateFips,
        string? countyFips,
        string? tract,
        string? blockGroup,
        string? block,
        string? geoid)
    {
        record.GeocodeStatus = status;
        record.MatchQuality = matchQuality;
        record.GeocoderSource = geocoderSource;
        record.MatchedAddress = matchedAddress;
        record.Latitude = latitude;
        record.Longitude = longitude;
        record.CensusStateFips = stateFips;
        record.CensusCountyFips = countyFips;
        record.CensusTract = tract;
        record.CensusBlockGroup = blockGroup;
        record.CensusBlock = block;
        record.CensusGeoid = geoid;
        record.GeocodeCandidates = null;
        record.GeocodedAt = DateTime.UtcNow;
    }

    public static void ApplyAmbiguous(DatasetRecord record, IReadOnlyList<GeocodeCandidate> candidates)
    {
        record.GeocodeStatus = "ambiguous";
        record.GeocodeCandidates = JsonSerializer.Serialize(candidates);
        record.GeocodedAt = DateTime.UtcNow;
    }

    public static void ApplyFailure(DatasetRecord record)
    {
        record.GeocodeStatus = "failed";
        record.GeocodeCandidates = null;
        record.GeocodedAt = DateTime.UtcNow;
    }

    /// <summary>Routes a GeocodeResult to the right branch above. successStatus
    /// distinguishes the automatic job's "matched" from a review action's
    /// "manually_resolved".</summary>
    public static void Apply(DatasetRecord record, GeocodeResult result, string successStatus, string matchQuality)
    {
        if (result.Ambiguous)
        {
            ApplyAmbiguous(record, result.Candidates!);
        }
        else if (result.Success)
        {
            ApplyMatch(
                record, successStatus, matchQuality, "census",
                result.MatchedAddress, result.Latitude!.Value, result.Longitude!.Value,
                result.StateFips, result.CountyFips, result.Tract, result.BlockGroup, result.Block, result.Geoid);
        }
        else
        {
            ApplyFailure(record);
        }
    }
}
