namespace GeoInsight.Application.Records;

public record DatasetRecordPointDto(
    long Id, decimal Latitude, decimal Longitude, string? ExternalId, string? Category,
    string? CensusTract, string? CensusGeoid);

public record DatasetRecordListItemDto(
    long Id,
    string? ExternalId,
    string AddressLine1,
    string City,
    string State,
    string? PostalCode,
    string? Category,
    decimal? Amount,
    DateOnly? RecordDate,
    string? Status,
    string GeocodeStatus);
