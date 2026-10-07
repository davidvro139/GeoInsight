using System.IO.Compression;
using GeoInsight.Application.Tracts;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoInsight.Infrastructure.Tracts;

/// <summary>
/// Downloads and parses the real, live, no-API-key Census TIGER/Line tract
/// shapefile for a whole state (TIGER only publishes per-state files, not
/// per-county) into raw parsed features. A thin I/O wrapper around
/// NetTopologySuite.IO.ShapeFile — not unit-tested (a multi-MB binary
/// shapefile isn't a reasonable test fixture), verified manually against the
/// real Utah file during development. Geometries come back as Polygon with
/// SRID 0; normalizing to MultiPolygon/SRID 4326 is the caller's job.
/// </summary>
public class TigerStateTractSource(HttpClient httpClient, ILogger<TigerStateTractSource> logger) : IStateTractSource
{
    public async Task<IReadOnlyList<ParsedTractFeature>> GetTractsAsync(string stateFips, CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"geoinsight-tract-import-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "tracts.zip");
            logger.LogInformation("Downloading TIGER tract shapefile for state {StateFips}", stateFips);

            await using (var responseStream = await httpClient.GetStreamAsync($"tl_2023_{stateFips}_tract.zip", cancellationToken))
            await using (var fileStream = File.Create(zipPath))
            {
                await responseStream.CopyToAsync(fileStream, cancellationToken);
            }

            var extractDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var shpPath = Directory.GetFiles(extractDir, "*.shp").Single();
            using var reader = new ShapefileDataReader(shpPath, GeometryFactory.Default);

            var features = new List<ParsedTractFeature>();
            while (reader.Read())
            {
                features.Add(new ParsedTractFeature(
                    Geoid: reader.GetString(reader.GetOrdinal("GEOID")),
                    CountyFips: reader.GetString(reader.GetOrdinal("COUNTYFP")),
                    TractCode: reader.GetString(reader.GetOrdinal("TRACTCE")),
                    TractName: reader.GetString(reader.GetOrdinal("NAMELSAD")),
                    Boundary: reader.Geometry));
            }

            logger.LogInformation("Parsed {Count} tract features for state {StateFips}", features.Count, stateFips);
            return features;
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
