namespace GeoInsight.Application.Imports;

/// <summary>
/// The standard dataset_record fields a CSV column can be mapped to — see
/// 02_PRODUCT_REQUIREMENTS.md §4. Kept as plain string keys (not an enum) so
/// this list can grow without a migration; the frontend mirrors these keys.
/// </summary>
public static class DatasetFieldCatalog
{
    public const string AddressLine1 = "address_line_1";
    public const string AddressLine2 = "address_line_2";
    public const string City = "city";
    public const string State = "state";
    public const string PostalCode = "postal_code";
    public const string ExternalId = "external_id";
    public const string Category = "category";
    public const string Amount = "amount";
    public const string Date = "date";
    public const string Status = "status";
    public const string Description = "description";

    /// <summary>
    /// Optional — geocoding (Phase 5) doesn't exist yet, so a CSV can supply
    /// pre-known coordinates directly. See DatasetRecordValidator for how a
    /// valid pair is distinguished from the "pending" default.
    /// </summary>
    public const string Latitude = "latitude";
    public const string Longitude = "longitude";

    public static readonly IReadOnlyList<string> RequiredFields = [AddressLine1, City, State];

    public static readonly IReadOnlyList<string> AllFields =
    [
        AddressLine1, AddressLine2, City, State, PostalCode,
        ExternalId, Category, Amount, Date, Status, Description,
        Latitude, Longitude,
    ];
}
