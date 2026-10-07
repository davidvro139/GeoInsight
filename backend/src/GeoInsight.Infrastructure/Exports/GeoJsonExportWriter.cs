using System.Text.Json;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Infrastructure.Exports;

/// <summary>
/// Only records with coordinates go in — same "geocoded records only" scope
/// as the map's own /records/points endpoint. Hand-built with
/// System.Text.Json rather than a GeoJSON library; the shape needed here
/// (a flat FeatureCollection of Points) is the same one the frontend's own
/// pointsToGeoJson already builds client-side.
/// </summary>
internal static class GeoJsonExportWriter
{
    public static Stream Write(IReadOnlyList<DatasetRecord> records, IReadOnlyDictionary<string, CensusDemographic> demographicsByGeoid)
    {
        var features = records
            .Where(r => r.Latitude is not null && r.Longitude is not null)
            .Select(r =>
            {
                var demographic = r.CensusGeoid is not null && demographicsByGeoid.TryGetValue(r.CensusGeoid, out var d) ? d : null;
                return new
                {
                    type = "Feature",
                    geometry = new { type = "Point", coordinates = new[] { r.Longitude, r.Latitude } },
                    properties = new
                    {
                        id = r.Id,
                        externalId = r.ExternalId,
                        category = r.Category,
                        amount = r.Amount,
                        date = r.RecordDate?.ToString("yyyy-MM-dd"),
                        status = r.Status,
                        geocodeStatus = r.GeocodeStatus,
                        censusTract = r.CensusTract,
                        censusGeoid = r.CensusGeoid,
                        tractPopulation = demographic?.Population,
                        tractMedianHouseholdIncome = demographic?.MedianHouseholdIncome,
                    },
                };
            })
            .ToList();

        var featureCollection = new { type = "FeatureCollection", features };

        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, featureCollection);
        stream.Position = 0;
        return stream;
    }
}
