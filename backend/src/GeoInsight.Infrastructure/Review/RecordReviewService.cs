using System.Text.Json;
using GeoInsight.Application.Common;
using GeoInsight.Application.Geocoding;
using GeoInsight.Application.Review;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Geocoding;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Infrastructure.Review;

public class RecordReviewService(
    GeoInsightDbContext dbContext, IDatasetAccessControl accessControl, IGeocodingService geocodingService, IAuditService auditService)
    : IRecordReviewService
{
    public async Task<ServiceResult<PagedResult<ReviewRecordDto>>> GetQueueAsync(
        long organizationId, long datasetId, long requestingUserId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<PagedResult<ReviewRecordDto>>.Failure(accessError.Type, accessError.Message);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = QueueQuery(datasetId);

        var totalCount = await query.CountAsync(cancellationToken);
        var records = await query
            .OrderBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResult<ReviewRecordDto>>.Success(
            new PagedResult<ReviewRecordDto>(records.Select(ToDto).ToList(), totalCount, page, pageSize));
    }

    public async Task<ServiceResult<ReviewRecordDto>> GetAsync(
        long organizationId, long datasetId, long recordId, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Viewer, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(accessError.Type, accessError.Message);
        }

        var record = await FindRecordAsync(datasetId, recordId, cancellationToken);
        return record is null
            ? ServiceResult<ReviewRecordDto>.Failure(ServiceErrorType.NotFound, "Record not found.")
            : ServiceResult<ReviewRecordDto>.Success(ToDto(record));
    }

    public async Task<ServiceResult<ReviewRecordDto>> AcceptCandidateAsync(
        long organizationId, long datasetId, long recordId, AcceptCandidateRequest request, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(accessError.Type, accessError.Message);
        }

        var record = await FindRecordAsync(datasetId, recordId, cancellationToken);
        if (record is null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(ServiceErrorType.NotFound, "Record not found.");
        }

        var candidates = ParseCandidates(record.GeocodeCandidates);
        if (request.CandidateIndex < 0 || request.CandidateIndex >= candidates.Count)
        {
            return ServiceResult<ReviewRecordDto>.Failure(ServiceErrorType.Validation, "Invalid candidate index.");
        }

        var candidate = candidates[request.CandidateIndex];
        GeocodeResultApplier.ApplyMatch(
            record, "manually_resolved", "candidate_selected", "census",
            candidate.MatchedAddress, candidate.Latitude, candidate.Longitude,
            candidate.StateFips, candidate.CountyFips, candidate.Tract, candidate.BlockGroup, candidate.Block, candidate.Geoid);

        auditService.Log(organizationId, requestingUserId, "record.accept_candidate", "dataset_record", recordId.ToString(),
            new { candidateIndex = request.CandidateIndex, candidate });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<ReviewRecordDto>.Success(ToDto(record));
    }

    public async Task<ServiceResult<ReviewRecordDto>> CorrectAddressAsync(
        long organizationId, long datasetId, long recordId, CorrectAddressRequest request, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(accessError.Type, accessError.Message);
        }

        var record = await FindRecordAsync(datasetId, recordId, cancellationToken);
        if (record is null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(ServiceErrorType.NotFound, "Record not found.");
        }

        var previousAddress = new { record.AddressLine1, record.AddressLine2, record.City, record.State, record.PostalCode };

        record.AddressLine1 = request.AddressLine1;
        record.AddressLine2 = request.AddressLine2;
        record.City = request.City;
        record.State = request.State;
        record.PostalCode = request.PostalCode;

        var result = await geocodingService.GeocodeAsync(
            new GeocodeQuery(request.AddressLine1, request.AddressLine2, request.City, request.State, request.PostalCode),
            cancellationToken);

        GeocodeResultApplier.Apply(record, result, successStatus: "manually_resolved", matchQuality: "exact");

        auditService.Log(organizationId, requestingUserId, "record.correct_address", "dataset_record", recordId.ToString(),
            new { previousAddress, newAddress = request, outcome = record.GeocodeStatus });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<ReviewRecordDto>.Success(ToDto(record));
    }

    public async Task<ServiceResult<ReviewRecordDto>> SetCoordinatesAsync(
        long organizationId, long datasetId, long recordId, SetCoordinatesRequest request, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(accessError.Type, accessError.Message);
        }

        var record = await FindRecordAsync(datasetId, recordId, cancellationToken);
        if (record is null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(ServiceErrorType.NotFound, "Record not found.");
        }

        GeocodeResultApplier.ApplyMatch(
            record, "manually_resolved", "manual", "manual",
            matchedAddress: null, request.Latitude, request.Longitude,
            stateFips: null, countyFips: null, tract: null, blockGroup: null, block: null, geoid: null);

        auditService.Log(organizationId, requestingUserId, "record.set_coordinates", "dataset_record", recordId.ToString(),
            new { request.Latitude, request.Longitude });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<ReviewRecordDto>.Success(ToDto(record));
    }

    public async Task<ServiceResult<ReviewRecordDto>> DismissAsync(
        long organizationId, long datasetId, long recordId, DismissRequest request, long requestingUserId, CancellationToken cancellationToken)
    {
        var accessError = await accessControl.RequireRoleAsync(
            organizationId, datasetId, requestingUserId, OrganizationRole.Analyst, cancellationToken);
        if (accessError is not null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(accessError.Type, accessError.Message);
        }

        var record = await FindRecordAsync(datasetId, recordId, cancellationToken);
        if (record is null)
        {
            return ServiceResult<ReviewRecordDto>.Failure(ServiceErrorType.NotFound, "Record not found.");
        }

        record.ReviewDismissedAt = DateTime.UtcNow;

        auditService.Log(organizationId, requestingUserId, "record.dismiss", "dataset_record", recordId.ToString(),
            new { request.Note });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<ReviewRecordDto>.Success(ToDto(record));
    }

    private IQueryable<DatasetRecord> QueueQuery(long datasetId) =>
        dbContext.DatasetRecords.Where(r =>
            r.DatasetId == datasetId
            && (r.GeocodeStatus == "failed" || r.GeocodeStatus == "ambiguous")
            && r.ReviewDismissedAt == null);

    private Task<DatasetRecord?> FindRecordAsync(long datasetId, long recordId, CancellationToken cancellationToken) =>
        dbContext.DatasetRecords.SingleOrDefaultAsync(r => r.Id == recordId && r.DatasetId == datasetId, cancellationToken);

    private static IReadOnlyList<GeocodeCandidate> ParseCandidates(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<GeocodeCandidate>>(json) ?? [];

    private static ReviewRecordDto ToDto(DatasetRecord record) => new(
        record.Id, record.ExternalId, record.AddressLine1, record.AddressLine2, record.City, record.State, record.PostalCode,
        record.NormalizedAddress, record.MatchedAddress, record.GeocodeStatus, record.MatchQuality, record.GeocoderSource,
        record.Latitude, record.Longitude, ParseCandidates(record.GeocodeCandidates), record.GeocodedAt);
}
