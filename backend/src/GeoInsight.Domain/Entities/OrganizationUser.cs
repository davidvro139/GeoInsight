namespace GeoInsight.Domain.Entities;

/// <summary>
/// Join entity between an Organization and a user account. The user account type
/// (ApplicationUser) lives in Infrastructure since it extends ASP.NET Identity's
/// IdentityUser, so this only stores the user's id to keep Domain framework-free.
/// </summary>
public class OrganizationUser
{
    public long OrganizationId { get; set; }

    public long UserId { get; set; }

    public OrganizationRole Role { get; set; }

    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
