using GeoInsight.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace GeoInsight.Api.Common;

public static class ServiceResultExtensions
{
    public static ActionResult<T> ToActionResult<T>(this ServiceResult<T> result, ControllerBase controller)
    {
        if (result.Succeeded)
        {
            return controller.Ok(result.Value);
        }

        var statusCode = result.Error!.Type switch
        {
            ServiceErrorType.Validation => StatusCodes.Status400BadRequest,
            ServiceErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ServiceErrorType.NotFound => StatusCodes.Status404NotFound,
            ServiceErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return controller.Problem(result.Error.Message, statusCode: statusCode);
    }
}
