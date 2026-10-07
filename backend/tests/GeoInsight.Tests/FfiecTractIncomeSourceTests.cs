using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Tracts;

namespace GeoInsight.Tests;

/// <summary>
/// Exercises the real bundled CSV (Tracts/Resources/ffiec-tract-income-2024-2026.csv,
/// trimmed from FFIEC's official Census Tract List) — not a fake — since the
/// whole point of this class is "does the real reference data parse correctly."
/// </summary>
public class FfiecTractIncomeSourceTests
{
    private static readonly FfiecTractIncomeSource Source = new();

    [Fact]
    public void Resolves_a_known_geoid_to_its_real_ffiec_classification()
    {
        // Callahan County, TX, tract 3010.01 — verified directly against the
        // downloaded FFIEC workbook while building this feature.
        var found = Source.TryGetValue("48059030101", out var record);

        Assert.True(found);
        Assert.Equal(TractIncomeTier.Middle, record.IncomeTier);
        Assert.Equal(61_923m, record.TractMedianFamilyIncome);
        Assert.Equal(68_388m, record.AreaMedianFamilyIncome);
        Assert.Equal(90.54m, record.IncomePercentageOfAreaMedian);
        Assert.Equal("ABILENE, TX", record.MsaMdName);
    }

    [Fact]
    public void An_unknown_geoid_is_not_found()
    {
        var found = Source.TryGetValue("00000000000", out _);

        Assert.False(found);
    }

    [Fact]
    public void A_tract_ffiec_could_not_classify_maps_to_the_unknown_tier_with_no_fabricated_zeros()
    {
        var found = Source.TryGetValue("48253020102", out var record);

        Assert.True(found);
        Assert.Equal(TractIncomeTier.Unknown, record.IncomeTier);
        Assert.Null(record.TractMedianFamilyIncome);
        Assert.Null(record.IncomePercentageOfAreaMedian);
    }
}
