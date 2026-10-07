using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Common;

public static class ControllerBaseExtensions
{
    public static long GetUserId(this ControllerBase controller)
    {
        var subject = controller.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("Request is missing the expected 'sub' claim.");

        return long.Parse(subject);
    }
}
