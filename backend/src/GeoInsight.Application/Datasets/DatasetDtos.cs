namespace GeoInsight.Application.Datasets;

public record CreateDatasetRequest(string Name, string? Description, string? DatasetType);

public record UpdateDatasetRequest(string Name, string? Description, string? DatasetType);

public record DatasetSummaryDto(
    long Id,
    string Name,
    string DatasetType,
    bool IsArchived,
    DateTime UpdatedAt,
    int RecordCount,
    int GeocodedCount);

public record DatasetDetailDto(
    long Id,
    string Name,
    string? Description,
    string DatasetType,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CreatedByDisplayName);
