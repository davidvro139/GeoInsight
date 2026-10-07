using Microsoft.AspNetCore.Identity;

namespace GeoInsight.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<long>
{
    public required string DisplayName { get; set; }

    public DateTime CreatedAt { get; set; }
}
