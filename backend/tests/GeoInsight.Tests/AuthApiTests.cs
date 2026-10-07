using System.Net;
using System.Net.Http.Json;
using GeoInsight.Application.Auth;

namespace GeoInsight.Tests;

/// <summary>
/// End-to-end through the real HTTP pipeline (see TestApiFactory) so
/// AddDefaultTokenProviders() and the real UserManager token round-trip are
/// actually exercised, not just the service method in isolation.
/// </summary>
public class AuthApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task Forgot_password_for_a_known_email_returns_a_usable_reset_token()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("reset-known@example.com", "Password123", "Reset Known"));

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest("reset-known@example.com"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.ResetToken));
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_still_returns_200_with_no_token()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest("nobody-at-all@example.com"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.Null(body!.ResetToken);
    }

    [Fact]
    public async Task Reset_password_with_a_real_token_changes_the_password_and_logs_in()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("reset-flow@example.com", "OldPassword123", "Reset Flow"));

        var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new ForgotPasswordRequest("reset-flow@example.com"));
        var token = (await forgot.Content.ReadFromJsonAsync<ForgotPasswordResponse>())!.ResetToken;

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest("reset-flow@example.com", token!, "NewPassword456"));
        reset.EnsureSuccessStatusCode();
        var authResponse = await reset.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.False(string.IsNullOrWhiteSpace(authResponse!.Token));

        var oldLogin = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("reset-flow@example.com", "OldPassword123"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("reset-flow@example.com", "NewPassword456"));
        newLogin.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reset_password_with_a_garbage_token_is_rejected()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("reset-bad-token@example.com", "Password123", "Reset Bad Token"));

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest("reset-bad-token@example.com", "not-a-real-token", "NewPassword456"));

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }
}
