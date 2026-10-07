namespace GeoInsight.Application.Auth;

public record RegisterRequest(string Email, string Password, string DisplayName);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string Token, DateTime ExpiresAtUtc, long UserId, string Email, string DisplayName);

public record ForgotPasswordRequest(string Email);

/// <summary>
/// ResetToken is null when no account matches the given email. There's no email
/// service in this project (see README/TODO decisions log) to deliver the token
/// invisibly, so — unlike a production forgot-password flow — this response's
/// shape unavoidably reveals whether the account exists. Documented trim, not
/// an oversight.
/// </summary>
public record ForgotPasswordResponse(string? ResetToken);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);
