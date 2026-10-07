using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using GeoInsight.Application.Common;
using GeoInsight.Application.Imports;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Imports;

public class ImportService(
    GeoInsightDbContext dbContext, IDatasetAccessControl accessControl, IFileStorageService fileStorage, IAuditService auditService)
    : IImportService
{
    private const int SampleRowCount = 10;
    private const int MaxReturnedIssues = 100;

    public async Task<ServiceResult<ImportUploadResult>> UploadAsync(
        long organizationId,
        long datasetId,
        long requestingUserId,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ImportUploadResult>.Failure(accessError.Type, accessError.Message);
        }

        using var memory = new MemoryStream();
        await fileStream.CopyToAsync(memory, cancellationToken);

        memory.Position = 0;
        var delimiter = DetectDelimiter(memory);

        memory.Position = 0;
        var (headers, sampleRows, totalRows) = ReadPreview(memory, delimiter);

        memory.Position = 0;
        var storedFileName = await fileStorage.SaveAsync(memory, fileName, cancellationToken);

        var import = new Import
        {
            DatasetId = datasetId,
            OriginalFileName = fileName,
            StoredFileName = storedFileName,
            Status = ImportStatus.Uploaded,
            TotalRows = totalRows,
            UploadedByUserId = requestingUserId,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.Imports.Add(import);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ImportUploadResult>.Success(
            new ImportUploadResult(import.Id, headers, sampleRows, delimiter, totalRows));
    }

    public Task<ServiceResult<ImportSummaryDto>> ValidateAsync(
        long organizationId,
        long datasetId,
        long importId,
        long requestingUserId,
        ImportValidateRequest request,
        CancellationToken cancellationToken) =>
        ProcessAndRespondAsync(organizationId, datasetId, importId, requestingUserId, request, commit: false, cancellationToken);

    public Task<ServiceResult<ImportSummaryDto>> CommitAsync(
        long organizationId,
        long datasetId,
        long importId,
        long requestingUserId,
        ImportValidateRequest request,
        CancellationToken cancellationToken) =>
        ProcessAndRespondAsync(organizationId, datasetId, importId, requestingUserId, request, commit: true, cancellationToken);

    public async Task<ServiceResult<IReadOnlyList<ImportListItemDto>>> GetAllAsync(
        long organizationId, long datasetId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<IReadOnlyList<ImportListItemDto>>.Failure(accessError.Type, accessError.Message);
        }

        var imports = await dbContext.Imports
            .Where(i => i.DatasetId == datasetId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = imports
            .Select(i => new ImportListItemDto(
                i.Id, i.OriginalFileName, i.Status.ToString(), i.TotalRows, i.ValidRows, i.WarningRows, i.InvalidRows,
                i.CreatedAt, i.CompletedAt))
            .ToList();

        return ServiceResult<IReadOnlyList<ImportListItemDto>>.Success(dtos);
    }

    private async Task<ServiceResult<ImportSummaryDto>> ProcessAndRespondAsync(
        long organizationId,
        long datasetId,
        long importId,
        long requestingUserId,
        ImportValidateRequest request,
        bool commit,
        CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ImportSummaryDto>.Failure(accessError.Type, accessError.Message);
        }

        var import = await dbContext.Imports.SingleOrDefaultAsync(
            i => i.Id == importId && i.DatasetId == datasetId, cancellationToken);
        if (import is null)
        {
            return ServiceResult<ImportSummaryDto>.Failure(ServiceErrorType.NotFound, "Import not found.");
        }

        var (total, valid, warning, invalid, issues, recordsToInsert) =
            await ProcessFileAsync(import, datasetId, request.Delimiter, request.Mappings, cancellationToken);

        import.TotalRows = total;
        import.ValidRows = valid;
        import.WarningRows = warning;
        import.InvalidRows = invalid;

        if (commit)
        {
            dbContext.DatasetRecords.AddRange(recordsToInsert);
            import.Status = ImportStatus.Committed;
            import.CompletedAt = DateTime.UtcNow;
            auditService.Log(organizationId, requestingUserId, "import.commit", "import", import.Id.ToString(),
                new { datasetId, totalRows = total, validRows = valid, warningRows = warning, invalidRows = invalid });
        }
        else
        {
            import.Status = ImportStatus.Validated;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ImportSummaryDto>.Success(
            new ImportSummaryDto(import.Id, import.Status.ToString(), total, valid, warning, invalid, issues));
    }

    private async Task<(int Total, int Valid, int Warning, int Invalid, List<ImportRowIssueDto> Issues, List<DatasetRecord> RecordsToInsert)>
        ProcessFileAsync(
            Import import, long datasetId, string delimiter, IReadOnlyList<ColumnMapping> mappings, CancellationToken cancellationToken)
    {
        using var stream = fileStorage.OpenRead(import.StoredFileName);
        using var textReader = new StreamReader(stream);
        using var csv = new CsvReader(textReader, CreateCsvConfiguration(delimiter));
        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];

        var existingExternalIds = await dbContext.DatasetRecords
            .Where(r => r.DatasetId == datasetId && r.ExternalId != null)
            .Select(r => r.ExternalId!)
            .ToHashSetAsync(cancellationToken);

        var seenExternalIdsInFile = new HashSet<string>();
        var seenRowSignatures = new HashSet<string>();
        var issues = new List<ImportRowIssueDto>();
        var recordsToInsert = new List<DatasetRecord>();
        int total = 0, valid = 0, warning = 0, invalid = 0, rowNumber = 1;

        while (csv.Read())
        {
            rowNumber++;
            total++;

            var rawValues = csv.Parser.Record ?? [];
            var isBlank = rawValues.All(string.IsNullOrWhiteSpace);

            var fields = new Dictionary<string, string?>();
            var customData = new Dictionary<string, string?>();
            for (var i = 0; i < headers.Length; i++)
            {
                var header = headers[i];
                var value = i < rawValues.Length ? rawValues[i] : null;
                var mapping = mappings.FirstOrDefault(m => m.CsvColumn == header);

                if (mapping?.SystemField is { } systemField && DatasetFieldCatalog.AllFields.Contains(systemField))
                {
                    fields[systemField] = value;
                }
                else
                {
                    customData[header] = value;
                }
            }

            var customJson = customData.Count > 0 ? JsonSerializer.Serialize(customData) : null;
            var result = DatasetRecordValidator.Validate(fields, customJson, isBlank);
            var messages = new List<string>(result.Messages);
            var severity = result.Severity;

            if (result.Record is not null)
            {
                if (result.Record.ExternalId is { } externalId)
                {
                    if (existingExternalIds.Contains(externalId) || !seenExternalIdsInFile.Add(externalId))
                    {
                        messages.Add($"Duplicate external ID '{externalId}'.");
                        severity = RowSeverity.Warning;
                    }
                }

                var signature = string.Join(
                    '|', result.Record.AddressLine1, result.Record.City, result.Record.State,
                    result.Record.PostalCode, result.Record.ExternalId, result.Record.Amount);
                if (!seenRowSignatures.Add(signature))
                {
                    messages.Add("Duplicate row (matches an earlier row in this file).");
                    severity = RowSeverity.Warning;
                }
            }

            switch (severity)
            {
                case RowSeverity.Invalid:
                    invalid++;
                    break;
                case RowSeverity.Warning:
                    warning++;
                    break;
                default:
                    valid++;
                    break;
            }

            foreach (var message in messages)
            {
                if (issues.Count < MaxReturnedIssues)
                {
                    issues.Add(new ImportRowIssueDto(rowNumber, severity.ToString(), message));
                }
            }

            if (result.Record is not null && severity != RowSeverity.Invalid)
            {
                result.Record.DatasetId = datasetId;
                result.Record.ImportId = import.Id;
                recordsToInsert.Add(result.Record);
            }
        }

        return (total, valid, warning, invalid, issues, recordsToInsert);
    }

    private static (string[] Headers, string[][] SampleRows, int TotalRows) ReadPreview(Stream stream, string delimiter)
    {
        using var textReader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(textReader, CreateCsvConfiguration(delimiter));
        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];

        var sample = new List<string[]>();
        var total = 0;
        while (csv.Read())
        {
            total++;
            if (sample.Count < SampleRowCount)
            {
                sample.Add(csv.Parser.Record ?? []);
            }
        }

        return (headers, sample.ToArray(), total);
    }

    private static string DetectDelimiter(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var headerLine = reader.ReadLine() ?? string.Empty;

        var candidates = new[] { ',', ';', '\t' };
        var best = candidates
            .Select(c => (Delimiter: c, Count: headerLine.Count(ch => ch == c)))
            .OrderByDescending(x => x.Count)
            .First();

        return (best.Count > 0 ? best.Delimiter : ',').ToString();
    }

    private static CsvConfiguration CreateCsvConfiguration(string delimiter) => new(CultureInfo.InvariantCulture)
    {
        Delimiter = delimiter,
        HasHeaderRecord = true,
        MissingFieldFound = null,
        BadDataFound = null,
    };
}
