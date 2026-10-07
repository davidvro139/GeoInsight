using GeoInsight.Application.Auth;
using GeoInsight.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace GeoInsight.Infrastructure.Auth;

public class AuthService(UserManager<ApplicationUser> userManager, IJwtTokenService jwtTokenService) : IAuthService
{
    private const string DemoEmail = "demo@geoinsight.app";
    private const string DemoPassword = "GeoInsightDemo123";
    private const string DemoDisplayName = "Demo User";

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return AuthResult.Failure("An account with this email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return AuthResult.Failure(createResult.Errors.Select(e => e.Description));
        }

        return AuthResult.Success(BuildAuthResponse(user));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return AuthResult.Failure("Invalid email or password.");
        }

        return AuthResult.Success(BuildAuthResponse(user));
    }

    public async Task<AuthResult> DemoLoginAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(DemoEmail);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                DisplayName = DemoDisplayName,
                CreatedAt = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user, DemoPassword);
            if (!createResult.Succeeded)
            {
                return AuthResult.Failure(createResult.Errors.Select(e => e.Description));
            }
        }

        return AuthResult.Success(BuildAuthResponse(user));
    }

    public async Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return new ForgotPasswordResponse(null);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return new ForgotPasswordResponse(token);
    }

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return AuthResult.Failure("Invalid or expired reset token.");
        }

        var resetResult = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            return AuthResult.Failure(resetResult.Errors.Select(e => e.Description));
        }

        return AuthResult.Success(BuildAuthResponse(user));
    }

    private AuthResponse BuildAuthResponse(ApplicationUser user)
    {
        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user.Id, user.Email!, user.DisplayName);
        return new AuthResponse(token, expiresAtUtc, user.Id, user.Email!, user.DisplayName);
    }
}
