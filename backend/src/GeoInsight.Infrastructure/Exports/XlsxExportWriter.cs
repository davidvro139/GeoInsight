using ClosedXML.Excel;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Infrastructure.Exports;

internal static class XlsxExportWriter
{
    private static readonly string[] Headers =
    [
        "External ID", "Address", "City", "State", "Category", "Amount", "Date", "Status",
        "Geocode Status", "Census Tract", "Census GEOID", "Tract Population", "Tract Median Household Income",
    ];

    public static Stream Write(IReadOnlyList<DatasetRecord> records, IReadOnlyDictionary<string, CensusDemographic> demographicsByGeoid)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Records");

        for (var col = 0; col < Headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = Headers[col];
        }

        var row = 2;
        foreach (var record in records)
        {
            var demographic = record.CensusGeoid is not null && demographicsByGeoid.TryGetValue(record.CensusGeoid, out var d) ? d : null;

            sheet.Cell(row, 1).Value = record.ExternalId;
            sheet.Cell(row, 2).Value = $"{record.AddressLine1}{(record.AddressLine2 is null ? "" : " " + record.AddressLine2)}";
            sheet.Cell(row, 3).Value = record.City;
            sheet.Cell(row, 4).Value = record.State;
            sheet.Cell(row, 5).Value = record.Category;
            if (record.Amount is { } amount) sheet.Cell(row, 6).Value = amount;
            sheet.Cell(row, 7).Value = record.RecordDate?.ToString("yyyy-MM-dd");
            sheet.Cell(row, 8).Value = record.Status;
            sheet.Cell(row, 9).Value = record.GeocodeStatus;
            sheet.Cell(row, 10).Value = record.CensusTract;
            sheet.Cell(row, 11).Value = record.CensusGeoid;
            if (demographic?.Population is { } population) sheet.Cell(row, 12).Value = population;
            if (demographic?.MedianHouseholdIncome is { } income) sheet.Cell(row, 13).Value = income;
            row++;
        }

        sheet.Columns().AdjustToContents();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
