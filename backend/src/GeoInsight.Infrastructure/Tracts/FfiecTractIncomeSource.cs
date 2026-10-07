using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using GeoInsight.Application.Tracts;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Infrastructure.Tracts;

/// <summary>
/// Backed by a bundled CSV (Tracts/Resources/ffiec-tract-income-2024-2026.csv)
/// trimmed from FFIEC's official Census Tract List
/// (ffiec.gov/sites/default/files/data/census/CensusTractList2026.xlsx,
/// "2024-2026 tracts" sheet) — real government reference data covering every
/// U.S. census tract, not derived/estimated. Registered as a singleton: 85k
/// rows of a handful of short columns is a few MB, trivial to hold in memory
/// for the process lifetime rather than re-parsing per request.
/// </summary>
public class FfiecTractIncomeSource : IFfiecTractIncomeSource
{
    private const string ResourceRelativePath = "Tracts/Resources/ffiec-tract-income-2024-2026.csv";

    private readonly Lazy<IReadOnlyDictionary<string, FfiecTractIncomeRecord>> records =
        new(LoadRecords, LazyThreadSafetyMode.ExecutionAndPublication);

    public bool TryGetValue(string geoid, out FfiecTractIncomeRecord record) =>
        records.Value.TryGetValue(geoid, out record!);

    private static IReadOnlyDictionary<string, FfiecTractIncomeRecord> LoadRecords()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ResourceRelativePath);
        using var textReader = new StreamReader(path);
        using var csv = new CsvReader(textReader, new CsvConfiguration(CultureInfo.InvariantCulture));

        csv.Read();
        csv.ReadHeader();

        var result = new Dictionary<string, FfiecTractIncomeRecord>();
        while (csv.Read())
        {
            var geoid = csv.GetField("Geoid")!;
            var tier = Enum.TryParse<TractIncomeTier>(csv.GetField("IncomeLevel"), out var parsed) ? parsed : TractIncomeTier.Unknown;

            result[geoid] = new FfiecTractIncomeRecord(
                Geoid: geoid,
                IncomeTier: tier,
                TractMedianFamilyIncome: ParseDecimal(csv.GetField("TractMfi")),
                AreaMedianFamilyIncome: ParseDecimal(csv.GetField("AreaMfi")),
                IncomePercentageOfAreaMedian: ParseDecimal(csv.GetField("IncomePercentage")),
                MsaMdName: csv.GetField("MsaMdName"));
        }

        return result;
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed != 0
            ? parsed
            : null;
}
