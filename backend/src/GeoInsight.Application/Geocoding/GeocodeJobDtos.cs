namespace GeoInsight.Application.Geocoding;

public record GeocodeJobDto(
    long Id,
    string Status,
    int TotalItems,
    int CompletedItems,
    int SuccessfulItems,
    int FailedItems,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    DateTime CreatedAt);
