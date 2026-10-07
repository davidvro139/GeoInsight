namespace GeoInsight.Domain.Entities;

/// <summary>
/// Roles are scoped per-organization membership, not global Identity roles —
/// see 02_PRODUCT_REQUIREMENTS.md section 1.
/// </summary>
public enum OrganizationRole
{
    Administrator,
    Analyst,
    Viewer
}

public static class OrganizationRoleExtensions
{
    // Explicit rank, deliberately independent of the enum's declared order above
    // (which follows the spec doc's listing order, not privilege order).
    private static readonly Dictionary<OrganizationRole, int> Rank = new()
    {
        [OrganizationRole.Viewer] = 1,
        [OrganizationRole.Analyst] = 2,
        [OrganizationRole.Administrator] = 3,
    };

    public static bool MeetsMinimum(this OrganizationRole role, OrganizationRole minimum) =>
        Rank[role] >= Rank[minimum];
}
