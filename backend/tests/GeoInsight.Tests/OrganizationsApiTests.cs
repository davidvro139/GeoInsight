using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoInsight.Application.Auth;
using GeoInsight.Application.Organizations;
using GeoInsight.Domain.Entities;

namespace GeoInsight.Tests;

/// <summary>
/// End-to-end through the real HTTP pipeline (see TestApiFactory) rather than
/// calling OrganizationService directly, specifically to catch the class of bug
/// that only shows up at the HTTP boundary: a JWT claim type the controller
/// doesn't actually read, or an enum that serializes as a raw int instead of
/// its string name (both happened once during development of this feature).
/// </summary>
public class OrganizationsApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    // Mirrors the server's AddJsonOptions (Program.cs) so the test's own
    // (de)serialization exercises the same string-enum behavior real API
    // clients would see, instead of falling back to System.Text.Json defaults.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // PropertyNameCaseInsensitive matters here: the server emits camelCase
        // (ASP.NET Core's MVC default), but these DTOs are plain C# records with
        // PascalCase constructor parameters — without this, System.Text.Json
        // silently leaves unmatched parameters at their default value instead of
        // throwing, which is how "token" came back as a null Token once already.
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string displayName)
    {
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "Password123", displayName));
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    [Fact]
    public async Task Creating_an_organization_returns_the_role_as_a_string()
    {
        var client = await CreateAuthenticatedClientAsync("string-enum@example.com", "String Enum Test");

        var response = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"myRole\":\"Administrator\"", body);

        var dto = await response.Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);
        Assert.Equal(OrganizationRole.Administrator, dto!.MyRole);
    }

    [Fact]
    public async Task Adding_a_member_with_a_string_role_round_trips_correctly()
    {
        var owner = await CreateAuthenticatedClientAsync("owner-roundtrip@example.com", "Owner");
        var invitee = await CreateAuthenticatedClientAsync("invitee-roundtrip@example.com", "Invitee");

        var org = await (await owner.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Acme")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        var addResponse = await owner.PostAsJsonAsync(
            $"/api/organizations/{org!.Id}/members",
            new AddMemberRequest("invitee-roundtrip@example.com", OrganizationRole.Analyst),
            JsonOptions);

        addResponse.EnsureSuccessStatusCode();
        var member = await addResponse.Content.ReadFromJsonAsync<OrganizationMemberDto>(JsonOptions);
        Assert.Equal(OrganizationRole.Analyst, member!.Role);

        var inviteeView = await invitee.GetAsync($"/api/organizations/{org.Id}");
        Assert.Equal(HttpStatusCode.OK, inviteeView.StatusCode);
    }

    [Fact]
    public async Task A_member_of_one_organization_gets_403_reading_another_organizations_members()
    {
        var ownerA = await CreateAuthenticatedClientAsync("owner-a-http@example.com", "Owner A");
        var ownerB = await CreateAuthenticatedClientAsync("owner-b-http@example.com", "Owner B");

        var orgA = await (await ownerA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Org A")))
            .Content.ReadFromJsonAsync<OrganizationDetailDto>(JsonOptions);

        var response = await ownerB.GetAsync($"/api/organizations/{orgA!.Id}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/organizations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
