using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.AssessmentArea;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Organizations;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Tests;

/// <summary>
/// HTTP-level permission/CRUD flow for an organization's self-declared CRA
/// Assessment Area — a plain org-scoped list of counties, no background job
/// involved (unlike CensusDataApiTests' standalone import), so a full round
/// trip (add, list, remove) can be asserted here directly.
/// </summary>
public class AssessmentAreaApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123", "Assessment Area Tester"));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    [Fact]
    public async Task An_administrator_can_add_list_and_remove_a_county()
    {
        var admin = await CreateAuthenticatedClientAsync("aa-admin@example.com");
        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        var addResponse = await admin.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/assessment-area/counties", new AssessmentAreaCountyDto("49", "057"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var counties = await (await admin.GetAsync($"/api/organizations/{org.Id}/assessment-area/counties"))
            .Content.ReadFromJsonAsync<List<AssessmentAreaCountyDto>>(JsonOptions);
        Assert.Contains(counties!, c => c.StateFips == "49" && c.CountyFips == "057");

        var removeResponse = await admin.DeleteAsync($"/api/organizations/{org.Id}/assessment-area/counties/49/057");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var countiesAfterRemove = await (await admin.GetAsync($"/api/organizations/{org.Id}/assessment-area/counties"))
            .Content.ReadFromJsonAsync<List<AssessmentAreaCountyDto>>(JsonOptions);
        Assert.DoesNotContain(countiesAfterRemove!, c => c.StateFips == "49" && c.CountyFips == "057");
    }

    [Fact]
    public async Task Adding_the_same_county_twice_does_not_duplicate_it()
    {
        var admin = await CreateAuthenticatedClientAsync("aa-dup-admin@example.com");
        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        await admin.PostAsJsonAsync($"/api/organizations/{org!.Id}/assessment-area/counties", new AssessmentAreaCountyDto("49", "057"), JsonOptions);
        await admin.PostAsJsonAsync($"/api/organizations/{org.Id}/assessment-area/counties", new AssessmentAreaCountyDto("49", "057"), JsonOptions);

        var counties = await (await admin.GetAsync($"/api/organizations/{org.Id}/assessment-area/counties"))
            .Content.ReadFromJsonAsync<List<AssessmentAreaCountyDto>>(JsonOptions);
        Assert.Single(counties!, c => c.StateFips == "49" && c.CountyFips == "057");
    }

    [Fact]
    public async Task Removing_a_county_that_was_never_added_is_a_no_op_success()
    {
        var admin = await CreateAuthenticatedClientAsync("aa-noop-admin@example.com");
        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        var response = await admin.DeleteAsync($"/api/organizations/{org!.Id}/assessment-area/counties/49/057");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_viewer_can_list_but_not_add_a_county()
    {
        var admin = await CreateAuthenticatedClientAsync("aa-owner@example.com");
        var viewer = await CreateAuthenticatedClientAsync("aa-viewer@example.com");
        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        await admin.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/members",
            new AddMemberRequest("aa-viewer@example.com", OrganizationRole.Viewer),
            JsonOptions);

        var listResponse = await viewer.GetAsync($"/api/organizations/{org.Id}/assessment-area/counties");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var addResponse = await viewer.PostAsJsonAsync(
            $"/api/organizations/{org.Id}/assessment-area/counties", new AssessmentAreaCountyDto("49", "057"), JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, addResponse.StatusCode);
    }

    [Fact]
    public async Task An_analyst_cannot_add_or_remove_a_county()
    {
        var admin = await CreateAuthenticatedClientAsync("aa-owner2@example.com");
        var analyst = await CreateAuthenticatedClientAsync("aa-analyst@example.com");
        var org = await (await admin.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        await admin.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/members",
            new AddMemberRequest("aa-analyst@example.com", OrganizationRole.Analyst),
            JsonOptions);

        var addResponse = await analyst.PostAsJsonAsync(
            $"/api/organizations/{org.Id}/assessment-area/counties", new AssessmentAreaCountyDto("49", "057"), JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, addResponse.StatusCode);

        var removeResponse = await analyst.DeleteAsync($"/api/organizations/{org.Id}/assessment-area/counties/49/057");
        Assert.Equal(HttpStatusCode.Forbidden, removeResponse.StatusCode);
    }

    [Fact]
    public async Task Assessment_areas_are_isolated_per_organization()
    {
        var adminA = await CreateAuthenticatedClientAsync("aa-org-a@example.com");
        var adminB = await CreateAuthenticatedClientAsync("aa-org-b@example.com");
        var orgA = await (await adminA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Org A")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        var orgB = await (await adminB.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Org B")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        await adminA.PostAsJsonAsync($"/api/organizations/{orgA!.Id}/assessment-area/counties", new AssessmentAreaCountyDto("49", "057"), JsonOptions);

        var orgBCounties = await (await adminB.GetAsync($"/api/organizations/{orgB!.Id}/assessment-area/counties"))
            .Content.ReadFromJsonAsync<List<AssessmentAreaCountyDto>>(JsonOptions);
        Assert.Empty(orgBCounties!);
    }
}
