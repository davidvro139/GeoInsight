using GeoInsight.Application.Common;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Application.Exports;

/// <summary>
/// Csv/Xlsx/GeoJson: the server generates the file itself from Filters.
/// Png/Pdf: the map/charts/PDF only exist as rendered pixels in the browser,
/// so the client renders them and sends the bytes here for the server to
/// just persist (FileContentBase64 is required for these two types).
/// </summary>
public record CreateExportRequest(ExportType ExportType, RecordFilterRequest? Filters, string? FileContentBase64);

public record ExportDto(long Id, string ExportType, string Status, DateTime CreatedAt, DateTime? CompletedAt);

public record ExportFileDto(Stream Content, string FileName, string ContentType);
