using GeoInsight.Application.Geocoding;

namespace GeoInsight.Application.Review;

public record ReviewRecordDto(
    long Id,
    string? ExternalId,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string? PostalCode,
    string? NormalizedAddress,
    string? MatchedAddress,
    string GeocodeStatus,
    string? MatchQuality,
    string? GeocoderSource,
    decimal? Latitude,
    decimal? Longitude,
    IReadOnlyList<GeocodeCandidate> Candidates,
    DateTime? GeocodedAt);

public record AcceptCandidateRequest(int CandidateIndex);

public record CorrectAddressRequest(string AddressLine1, string? AddressLine2, string City, string State, string? PostalCode);

public record SetCoordinatesRequest(decimal Latitude, decimal Longitude);

public record DismissRequest(string? Note);
