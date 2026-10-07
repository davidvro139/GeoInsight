using GeoInsight.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Response) : Problem(string.Join(" ", result.Errors), statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Response) : Problem(string.Join(" ", result.Errors), statusCode: StatusCodes.Status401Unauthorized);
    }

    [HttpPost("demo-login")]
    public async Task<ActionResult<AuthResponse>> DemoLogin(CancellationToken cancellationToken)
    {
        var result = await authService.DemoLoginAsync(cancellationToken);
        return result.Succeeded ? Ok(result.Response) : Problem(string.Join(" ", result.Errors), statusCode: StatusCodes.Status500InternalServerError);
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.ForgotPasswordAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<AuthResponse>> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.ResetPasswordAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Response) : Problem(string.Join(" ", result.Errors), statusCode: StatusCodes.Status400BadRequest);
    }
}
