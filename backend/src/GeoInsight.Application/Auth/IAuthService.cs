namespace GeoInsight.Application.Auth;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Finds-or-creates a fixed demo account and logs into it. Lets the "try the
    /// demo" button on the login page work against a freshly-migrated database
    /// with no manual seeding — a stand-in for the real seeded demo org in
    /// TODO.md Phase 15.
    /// </summary>
    Task<AuthResult> DemoLoginAsync(CancellationToken cancellationToken);

    Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);

    Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
