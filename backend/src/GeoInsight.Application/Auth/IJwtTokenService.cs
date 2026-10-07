namespace GeoInsight.Application.Auth;

public interface IJwtTokenService
{
    /// <summary>
    /// Issues a signed JWT for the given user identity. Kept to primitive
    /// parameters (rather than taking ApplicationUser) so Application does not
    /// need a reference to Infrastructure/Identity.
    /// </summary>
    (string Token, DateTime ExpiresAtUtc) CreateToken(long userId, string email, string displayName);
}
