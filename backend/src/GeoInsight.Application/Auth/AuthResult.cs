namespace GeoInsight.Application.Auth;

public class AuthResult
{
    public bool Succeeded { get; }

    public AuthResponse? Response { get; }

    public IReadOnlyList<string> Errors { get; }

    private AuthResult(bool succeeded, AuthResponse? response, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Response = response;
        Errors = errors;
    }

    public static AuthResult Success(AuthResponse response) => new(true, response, []);

    public static AuthResult Failure(params IEnumerable<string> errors) => new(false, null, errors.ToList());
}
