using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoInsight.Tests;

/// <summary>
/// Stands in for a live-MySQL integration test: no MySQL server is available in
/// this environment, so this exercises the same DbContext/model against EF
/// Core's InMemory provider instead.
/// </summary>
public class GeoInsightDbContextTests
{
    private static GeoInsightDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GeoInsightDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new GeoInsightDbContext(options);
    }

    [Fact]
    public async Task Can_save_and_read_an_organization_with_a_member()
    {
        await using var context = CreateContext();

        var user = new ApplicationUser
        {
            UserName = "admin@example.com",
            Email = "admin@example.com",
            DisplayName = "Admin User"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var organization = new Organization { Name = "Northern Utah Community Bank" };
        organization.Members.Add(new OrganizationUser
        {
            UserId = user.Id,
            Role = OrganizationRole.Administrator,
            CreatedAt = DateTime.UtcNow
        });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var reloaded = await context.Organizations
            .Include(o => o.Members)
            .SingleAsync(o => o.Id == organization.Id);

        Assert.Single(reloaded.Members);
        Assert.Equal(OrganizationRole.Administrator, reloaded.Members.Single().Role);
    }
}
