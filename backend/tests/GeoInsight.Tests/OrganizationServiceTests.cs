using GeoInsight.Application.Common;
using GeoInsight.Application.Organizations;
using GeoInsight.Domain.Entities;
using GeoInsight.Infrastructure.Common;
using GeoInsight.Infrastructure.Identity;
using GeoInsight.Infrastructure.Organizations;
using GeoInsight.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoInsight.Tests;

/// <summary>
/// Exercises OrganizationService against an EF Core InMemory GeoInsightDbContext
/// and a real UserManager built the same way production DI builds it (see
/// GeoInsight.Infrastructure.DependencyInjection.AddInfrastructure) — no MySQL
/// needed, same reasoning as GeoInsightDbContextTests.
/// </summary>
public class OrganizationServiceTests
{
    private sealed class Harness : IAsyncDisposable
    {
        public required GeoInsightDbContext DbContext { get; init; }
        public required UserManager<ApplicationUser> UserManager { get; init; }
        public required OrganizationService Service { get; init; }
        public required ServiceProvider Provider { get; init; }

        public static Harness Create()
        {
            var services = new ServiceCollection();
            services.AddDbContext<GeoInsightDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddIdentityCore<ApplicationUser>(options =>
                {
                    options.Password.RequiredLength = 8;
                    options.Password.RequireNonAlphanumeric = false;
                    options.User.RequireUniqueEmail = true;
                })
                .AddEntityFrameworkStores<GeoInsightDbContext>();
            services.AddDataProtection();

            var provider = services.BuildServiceProvider();
            var dbContext = provider.GetRequiredService<GeoInsightDbContext>();
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var dataProtection = provider.GetRequiredService<IDataProtectionProvider>();

            return new Harness
            {
                Provider = provider,
                DbContext = dbContext,
                UserManager = userManager,
                Service = new OrganizationService(dbContext, userManager, new OrganizationAccessControl(dbContext), new AuditService(dbContext), dataProtection),
            };
        }

        public async Task<ApplicationUser> CreateUserAsync(string email, string displayName)
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName };
            var result = await UserManager.CreateAsync(user, "Password123");
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
            }

            return user;
        }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    [Fact]
    public async Task Create_makes_the_creator_an_administrator()
    {
        await using var h = Harness.Create();
        var owner = await h.CreateUserAsync("owner@example.com", "Owner");

        var result = await h.Service.CreateAsync(owner.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OrganizationRole.Administrator, result.Value!.MyRole);
    }

    [Fact]
    public async Task GetById_for_a_non_member_is_forbidden()
    {
        await using var h = Harness.Create();
        var owner = await h.CreateUserAsync("owner@example.com", "Owner");
        var outsider = await h.CreateUserAsync("outsider@example.com", "Outsider");
        var org = (await h.Service.CreateAsync(owner.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;

        var result = await h.Service.GetByIdAsync(org.Id, outsider.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task GetById_for_a_missing_organization_is_not_found()
    {
        await using var h = Harness.Create();
        var user = await h.CreateUserAsync("user@example.com", "User");

        var result = await h.Service.GetByIdAsync(999, user.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Administrator_can_add_an_existing_user_by_email()
    {
        await using var h = Harness.Create();
        var admin = await h.CreateUserAsync("admin@example.com", "Admin");
        var newMember = await h.CreateUserAsync("analyst@example.com", "Analyst Person");
        var org = (await h.Service.CreateAsync(admin.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;

        var result = await h.Service.AddMemberAsync(
            org.Id, admin.Id, new AddMemberRequest(newMember.Email!, OrganizationRole.Analyst), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OrganizationRole.Analyst, result.Value!.Role);
    }

    [Fact]
    public async Task Changing_a_members_role_logs_the_previous_and_new_role()
    {
        await using var h = Harness.Create();
        var admin = await h.CreateUserAsync("admin@example.com", "Admin");
        var member = await h.CreateUserAsync("viewer@example.com", "Viewer Person");
        var org = (await h.Service.CreateAsync(admin.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;
        await h.Service.AddMemberAsync(org.Id, admin.Id, new AddMemberRequest(member.Email!, OrganizationRole.Viewer), CancellationToken.None);

        var result = await h.Service.ChangeMemberRoleAsync(
            org.Id, admin.Id, member.Id, new ChangeMemberRoleRequest(OrganizationRole.Analyst), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OrganizationRole.Analyst, result.Value!.Role);

        var auditEvent = h.DbContext.AuditEvents.Single(e => e.Action == "organization.change_role");
        Assert.Equal("organization_user", auditEvent.EntityType);
        Assert.Equal(member.Id.ToString(), auditEvent.EntityId);
        Assert.Contains("\"previousRole\":\"Viewer\"", auditEvent.Metadata);
        Assert.Contains("\"newRole\":\"Analyst\"", auditEvent.Metadata);
    }

    [Fact]
    public async Task Adding_an_unknown_email_is_a_validation_error()
    {
        await using var h = Harness.Create();
        var admin = await h.CreateUserAsync("admin@example.com", "Admin");
        var org = (await h.Service.CreateAsync(admin.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;

        var result = await h.Service.AddMemberAsync(
            org.Id, admin.Id, new AddMemberRequest("nobody@example.com", OrganizationRole.Viewer), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task A_non_administrator_cannot_add_members()
    {
        await using var h = Harness.Create();
        var admin = await h.CreateUserAsync("admin@example.com", "Admin");
        var viewer = await h.CreateUserAsync("viewer@example.com", "Viewer Person");
        var candidate = await h.CreateUserAsync("candidate@example.com", "Candidate");
        var org = (await h.Service.CreateAsync(admin.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;
        await h.Service.AddMemberAsync(org.Id, admin.Id, new AddMemberRequest(viewer.Email!, OrganizationRole.Viewer), CancellationToken.None);

        var result = await h.Service.AddMemberAsync(
            org.Id, viewer.Id, new AddMemberRequest(candidate.Email!, OrganizationRole.Viewer), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task Cannot_demote_the_last_administrator()
    {
        await using var h = Harness.Create();
        var admin = await h.CreateUserAsync("admin@example.com", "Admin");
        var org = (await h.Service.CreateAsync(admin.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;

        var result = await h.Service.ChangeMemberRoleAsync(
            org.Id, admin.Id, admin.Id, new ChangeMemberRoleRequest(OrganizationRole.Analyst), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Cannot_remove_the_last_administrator()
    {
        await using var h = Harness.Create();
        var admin = await h.CreateUserAsync("admin@example.com", "Admin");
        var org = (await h.Service.CreateAsync(admin.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;

        var result = await h.Service.RemoveMemberAsync(org.Id, admin.Id, admin.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task A_member_of_one_organization_cannot_view_another_organizations_members()
    {
        await using var h = Harness.Create();
        var adminA = await h.CreateUserAsync("admin-a@example.com", "Admin A");
        var adminB = await h.CreateUserAsync("admin-b@example.com", "Admin B");
        var orgA = (await h.Service.CreateAsync(adminA.Id, new CreateOrganizationRequest("Org A"), CancellationToken.None)).Value!;
        await h.Service.CreateAsync(adminB.Id, new CreateOrganizationRequest("Org B"), CancellationToken.None);

        var result = await h.Service.GetMembersAsync(orgA.Id, adminB.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task An_administrator_can_set_and_clear_the_census_api_key()
    {
        await using var h = Harness.Create();
        var admin = await h.CreateUserAsync("admin@example.com", "Admin");
        var org = (await h.Service.CreateAsync(admin.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;
        Assert.False(org.HasCensusApiKey);

        var setResult = await h.Service.SetCensusApiKeyAsync(org.Id, admin.Id, "real-census-key", CancellationToken.None);
        Assert.True(setResult.Succeeded);

        var afterSet = (await h.Service.GetByIdAsync(org.Id, admin.Id, CancellationToken.None)).Value!;
        Assert.True(afterSet.HasCensusApiKey);

        var storedEncrypted = await h.DbContext.Organizations.Where(o => o.Id == org.Id).Select(o => o.CensusApiKeyEncrypted).SingleAsync();
        Assert.NotNull(storedEncrypted);
        Assert.DoesNotContain("real-census-key", storedEncrypted); // encrypted at rest, not plaintext

        var clearResult = await h.Service.SetCensusApiKeyAsync(org.Id, admin.Id, null, CancellationToken.None);
        Assert.True(clearResult.Succeeded);

        var afterClear = (await h.Service.GetByIdAsync(org.Id, admin.Id, CancellationToken.None)).Value!;
        Assert.False(afterClear.HasCensusApiKey);
    }

    [Fact]
    public async Task A_non_administrator_cannot_set_the_census_api_key()
    {
        await using var h = Harness.Create();
        var admin = await h.CreateUserAsync("admin@example.com", "Admin");
        var viewer = await h.CreateUserAsync("viewer@example.com", "Viewer");
        var org = (await h.Service.CreateAsync(admin.Id, new CreateOrganizationRequest("Acme"), CancellationToken.None)).Value!;
        await h.Service.AddMemberAsync(org.Id, admin.Id, new AddMemberRequest(viewer.Email!, OrganizationRole.Viewer), CancellationToken.None);

        var result = await h.Service.SetCensusApiKeyAsync(org.Id, viewer.Id, "sneaky-key", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorType.Forbidden, result.Error!.Type);
    }
}
