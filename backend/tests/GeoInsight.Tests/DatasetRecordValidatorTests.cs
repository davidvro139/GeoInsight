using GeoInsight.Application.Imports;
using GeoInsight.Infrastructure.Imports;

namespace GeoInsight.Tests;

public class DatasetRecordValidatorTests
{
    private static Dictionary<string, string?> ValidFields() => new()
    {
        [DatasetFieldCatalog.AddressLine1] = "123 Main St",
        [DatasetFieldCatalog.City] = "Ogden",
        [DatasetFieldCatalog.State] = "UT",
        [DatasetFieldCatalog.PostalCode] = "84401",
    };

    [Fact]
    public void A_fully_valid_row_is_valid()
    {
        var result = DatasetRecordValidator.Validate(ValidFields(), null, isBlankRow: false);

        Assert.Equal(RowSeverity.Valid, result.Severity);
        Assert.NotNull(result.Record);
        Assert.Equal("UT", result.Record!.State);
        Assert.Equal("pending", result.Record.GeocodeStatus);
        Assert.Null(result.Record.Latitude);
    }

    [Fact]
    public void A_blank_row_is_invalid()
    {
        var result = DatasetRecordValidator.Validate(new Dictionary<string, string?>(), null, isBlankRow: true);

        Assert.Equal(RowSeverity.Invalid, result.Severity);
        Assert.Null(result.Record);
    }

    [Theory]
    [InlineData(DatasetFieldCatalog.AddressLine1)]
    [InlineData(DatasetFieldCatalog.City)]
    [InlineData(DatasetFieldCatalog.State)]
    public void Missing_a_required_field_is_invalid(string requiredField)
    {
        var fields = ValidFields();
        fields[requiredField] = "";

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Invalid, result.Severity);
        Assert.Null(result.Record);
    }

    [Fact]
    public void An_unrecognized_state_code_is_a_warning_not_invalid()
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.State] = "ZZ";

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Warning, result.Severity);
        Assert.NotNull(result.Record);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("abcde")]
    [InlineData("84401-123")]
    public void A_malformed_postal_code_is_a_warning(string postalCode)
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.PostalCode] = postalCode;

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Warning, result.Severity);
    }

    [Fact]
    public void A_zip_plus_four_postal_code_is_valid()
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.PostalCode] = "84401-1234";

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Valid, result.Severity);
    }

    [Fact]
    public void An_unparseable_amount_is_a_warning_and_amount_is_left_null()
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.Amount] = "not-a-number";

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Warning, result.Severity);
        Assert.Null(result.Record!.Amount);
    }

    [Fact]
    public void A_valid_amount_parses_correctly()
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.Amount] = "250000.50";

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Valid, result.Severity);
        Assert.Equal(250000.50m, result.Record!.Amount);
    }

    [Fact]
    public void An_unparseable_date_is_a_warning_and_date_is_left_null()
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.Date] = "not-a-date";

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Warning, result.Severity);
        Assert.Null(result.Record!.RecordDate);
    }

    [Fact]
    public void A_valid_coordinate_pair_is_marked_matched_via_csv_import()
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.Latitude] = "41.2230";
        fields[DatasetFieldCatalog.Longitude] = "-111.9738";

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Valid, result.Severity);
        Assert.Equal(41.2230m, result.Record!.Latitude);
        Assert.Equal(-111.9738m, result.Record.Longitude);
        Assert.Equal("matched", result.Record.GeocodeStatus);
        Assert.Equal("csv_import", result.Record.GeocoderSource);
    }

    [Fact]
    public void An_incomplete_coordinate_pair_is_a_warning_and_both_are_left_null()
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.Latitude] = "41.2230";
        // longitude intentionally omitted

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Warning, result.Severity);
        Assert.Null(result.Record!.Latitude);
        Assert.Null(result.Record.Longitude);
        Assert.Equal("pending", result.Record.GeocodeStatus);
    }

    [Theory]
    [InlineData("91", "-111.9738")] // latitude out of range
    [InlineData("41.2230", "-181")] // longitude out of range
    [InlineData("not-a-number", "-111.9738")]
    public void An_invalid_coordinate_pair_is_a_warning_and_both_are_left_null(string latitude, string longitude)
    {
        var fields = ValidFields();
        fields[DatasetFieldCatalog.Latitude] = latitude;
        fields[DatasetFieldCatalog.Longitude] = longitude;

        var result = DatasetRecordValidator.Validate(fields, null, isBlankRow: false);

        Assert.Equal(RowSeverity.Warning, result.Severity);
        Assert.Null(result.Record!.Latitude);
        Assert.Null(result.Record.Longitude);
    }

    [Fact]
    public void Unmapped_columns_are_carried_through_as_custom_data()
    {
        var result = DatasetRecordValidator.Validate(ValidFields(), """{"loan_officer":"Jane Doe"}""", isBlankRow: false);

        Assert.Equal("""{"loan_officer":"Jane Doe"}""", result.Record!.CustomData);
    }
}
