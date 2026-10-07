namespace GeoInsight.Domain.Entities;

public class Organization
{
    public long Id { get; set; }

    public required string Name { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Encrypted (via IDataProtector) Census ACS Data API key, set by an
    /// Administrator in Organization Settings. Null means "use synthetic
    /// placeholder demographics" — see IDemographicDataSource.
    /// </summary>
    public string? CensusApiKeyEncrypted { get; set; }

    public ICollection<OrganizationUser> Members { get; set; } = new List<OrganizationUser>();
}
