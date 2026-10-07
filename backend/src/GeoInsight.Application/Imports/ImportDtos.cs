namespace GeoInsight.Application.Imports;

/// <summary>
/// SystemField is one of DatasetFieldCatalog's standard keys, or null/blank
/// meaning "unmapped" — unmapped columns land in DatasetRecord.CustomData
/// keyed by CsvColumn (see 02_PRODUCT_REQUIREMENTS.md §4 "define custom fields").
/// </summary>
public record ColumnMapping(string CsvColumn, string? SystemField);

public record ImportUploadResult(
    long ImportId, string[] Headers, string[][] SampleRows, string DetectedDelimiter, int TotalRows);

public record ImportValidateRequest(string Delimiter, IReadOnlyList<ColumnMapping> Mappings);

public record ImportRowIssueDto(int RowNumber, string Severity, string Message);

public record ImportSummaryDto(
    long ImportId,
    string Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    IReadOnlyList<ImportRowIssueDto> Issues);

public record ImportListItemDto(
    long Id,
    string OriginalFileName,
    string Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    DateTime CreatedAt,
    DateTime? CompletedAt);
