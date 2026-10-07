using System.Globalization;
using CsvHelper;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Infrastructure.Exports;

/// <summary>
/// CsvHelper is already a dependency (Phase 3 uses it to read CSV imports) —
/// this is the first place it writes one.
/// </summary>
internal static class CsvExportWriter
{
    public static async Task<Stream> WriteAsync(
        IReadOnlyList<DatasetRecord> records, IReadOnlyDictionary<string, CensusDemographic> demographicsByGeoid, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, leaveOpen: true))
        await using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteField("external_id");
            csv.WriteField("address");
            csv.WriteField("city");
            csv.WriteField("state");
            csv.WriteField("category");
            csv.WriteField("amount");
            csv.WriteField("date");
            csv.WriteField("status");
            csv.WriteField("geocode_status");
            csv.WriteField("census_tract");
            csv.WriteField("census_geoid");
            csv.WriteField("tract_population");
            csv.WriteField("tract_median_household_income");
            await csv.NextRecordAsync();

            foreach (var record in records)
            {
                var demographic = record.CensusGeoid is not null && demographicsByGeoid.TryGetValue(record.CensusGeoid, out var d) ? d : null;

                csv.WriteField(record.ExternalId);
                csv.WriteField($"{record.AddressLine1}{(record.AddressLine2 is null ? "" : " " + record.AddressLine2)}");
                csv.WriteField(record.City);
                csv.WriteField(record.State);
                csv.WriteField(record.Category);
                csv.WriteField(record.Amount);
                csv.WriteField(record.RecordDate?.ToString("yyyy-MM-dd"));
                csv.WriteField(record.Status);
                csv.WriteField(record.GeocodeStatus);
                csv.WriteField(record.CensusTract);
                csv.WriteField(record.CensusGeoid);
                csv.WriteField(demographic?.Population);
                csv.WriteField(demographic?.MedianHouseholdIncome);
                await csv.NextRecordAsync();
            }
        }

        stream.Position = 0;
        return stream;
    }
}
