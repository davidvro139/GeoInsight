using System.Globalization;
using System.Text.RegularExpressions;
using GeoInsight.Application.Imports;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Infrastructure.Imports;

public enum RowSeverity
{
    Valid,
    Warning,
    Invalid,
}

public record RowValidationResult(RowSeverity Severity, IReadOnlyList<string> Messages, DatasetRecord? Record);

/// <summary>
/// Row-level validation rules for CSV import — see the "Validation severity"
/// design decision: Invalid rows are excluded from commit, Warning rows are
/// still committed but flagged.
/// </summary>
public static partial class DatasetRecordValidator
{
    private static readonly HashSet<string> UspsStateCodes =
    [
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA",
        "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD",
        "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
        "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC",
        "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
        "DC", "PR", "GU", "VI", "AS", "MP",
    ];

    [GeneratedRegex(@"^\d{5}(-\d{4})?$")]
    private static partial Regex ZipCodeRegex();

    public static RowValidationResult Validate(
        IReadOnlyDictionary<string, string?> fields, string? customDataJson, bool isBlankRow)
    {
        var messages = new List<string>();

        if (isBlankRow)
        {
            return new RowValidationResult(RowSeverity.Invalid, ["Row is blank."], null);
        }

        var addressLine1 = fields.GetValueOrDefault(DatasetFieldCatalog.AddressLine1)?.Trim();
        var city = fields.GetValueOrDefault(DatasetFieldCatalog.City)?.Trim();
        var state = fields.GetValueOrDefault(DatasetFieldCatalog.State)?.Trim();

        if (string.IsNullOrEmpty(addressLine1))
        {
            messages.Add("Missing address line 1.");
        }
        if (string.IsNullOrEmpty(city))
        {
            messages.Add("Missing city.");
        }
        if (string.IsNullOrEmpty(state))
        {
            messages.Add("Missing state.");
        }

        if (messages.Count > 0)
        {
            return new RowValidationResult(RowSeverity.Invalid, messages, null);
        }

        var isWarning = false;
        var normalizedState = state!.ToUpperInvariant();
        if (!UspsStateCodes.Contains(normalizedState))
        {
            messages.Add($"'{state}' is not a recognized state code.");
            isWarning = true;
        }

        var postalCode = fields.GetValueOrDefault(DatasetFieldCatalog.PostalCode)?.Trim();
        if (!string.IsNullOrEmpty(postalCode) && !ZipCodeRegex().IsMatch(postalCode))
        {
            messages.Add($"'{postalCode}' is not a valid US postal code.");
            isWarning = true;
        }

        decimal? amount = null;
        var rawAmount = fields.GetValueOrDefault(DatasetFieldCatalog.Amount)?.Trim();
        if (!string.IsNullOrEmpty(rawAmount))
        {
            if (decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount))
            {
                amount = parsedAmount;
            }
            else
            {
                messages.Add($"'{rawAmount}' is not a valid amount.");
                isWarning = true;
            }
        }

        DateOnly? recordDate = null;
        var rawDate = fields.GetValueOrDefault(DatasetFieldCatalog.Date)?.Trim();
        if (!string.IsNullOrEmpty(rawDate))
        {
            if (DateOnly.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                recordDate = parsedDate;
            }
            else
            {
                messages.Add($"'{rawDate}' is not a valid date.");
                isWarning = true;
            }
        }

        var (latitude, longitude, geocodeStatus, geocoderSource) = ParseCoordinates(fields, messages, ref isWarning);

        var now = DateTime.UtcNow;
        var record = new DatasetRecord
        {
            AddressLine1 = addressLine1!,
            AddressLine2 = fields.GetValueOrDefault(DatasetFieldCatalog.AddressLine2)?.Trim(),
            City = city!,
            State = normalizedState,
            PostalCode = string.IsNullOrEmpty(postalCode) ? null : postalCode,
            ExternalId = NullIfEmpty(fields.GetValueOrDefault(DatasetFieldCatalog.ExternalId)),
            Category = NullIfEmpty(fields.GetValueOrDefault(DatasetFieldCatalog.Category)),
            Amount = amount,
            RecordDate = recordDate,
            Status = NullIfEmpty(fields.GetValueOrDefault(DatasetFieldCatalog.Status)),
            Description = NullIfEmpty(fields.GetValueOrDefault(DatasetFieldCatalog.Description)),
            Latitude = latitude,
            Longitude = longitude,
            GeocodeStatus = geocodeStatus,
            GeocoderSource = geocoderSource,
            CustomData = customDataJson,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return new RowValidationResult(isWarning ? RowSeverity.Warning : RowSeverity.Valid, messages, record);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Geocoding (Phase 5) doesn't exist yet, so a CSV can supply coordinates
    /// directly. A valid pair is marked "matched"/"csv_import" so a future
    /// geocoding job knows to skip it; anything incomplete or invalid is a
    /// warning, and both fields are left null rather than storing a bad point.
    /// </summary>
    private static (decimal? Latitude, decimal? Longitude, string GeocodeStatus, string? GeocoderSource) ParseCoordinates(
        IReadOnlyDictionary<string, string?> fields, List<string> messages, ref bool isWarning)
    {
        var rawLatitude = fields.GetValueOrDefault(DatasetFieldCatalog.Latitude)?.Trim();
        var rawLongitude = fields.GetValueOrDefault(DatasetFieldCatalog.Longitude)?.Trim();

        if (string.IsNullOrEmpty(rawLatitude) && string.IsNullOrEmpty(rawLongitude))
        {
            return (null, null, "pending", null);
        }

        var latitudeOk = decimal.TryParse(rawLatitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && latitude is >= -90 and <= 90;
        var longitudeOk = decimal.TryParse(rawLongitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            && longitude is >= -180 and <= 180;

        if (latitudeOk && longitudeOk)
        {
            return (latitude, longitude, "matched", "csv_import");
        }

        messages.Add($"'{rawLatitude}, {rawLongitude}' is not a valid coordinate pair; ignored.");
        isWarning = true;
        return (null, null, "pending", null);
    }
}
