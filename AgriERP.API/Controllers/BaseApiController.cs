using AgriERP.Application.Common.Exceptions;
using AgriERP.Shared.Constants;
using AgriERP.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// The authenticated user's id. Throws rather than returning null: every
    /// action reaching this is behind [Authorize], so a missing id means the
    /// token pipeline is broken, and failing loudly beats writing audit rows
    /// with a null user.
    /// </summary>
    protected int CurrentUserId
        => int.TryParse(User.FindFirst(AgriClaimTypes.UserId)?.Value, out var id)
            ? id
            : throw new UnauthorizedException("The access token does not carry a user id.");

    protected string? IpAddress
    {
        get
        {
            var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            return !string.IsNullOrWhiteSpace(forwarded)
                ? forwarded.Split(',')[0].Trim()
                : HttpContext.Connection.RemoteIpAddress?.ToString();
        }
    }

    protected string? UserAgent => Request.Headers.UserAgent.FirstOrDefault();

    protected IActionResult Success<T>(T data, string? message = null)
        => Ok(ApiResponse<T>.Ok(data, message));

    protected IActionResult Success(string message)
        => Ok(ApiResponse.Ok(message));

    protected IActionResult SuccessCreated<T>(T data, string message)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<T>.Ok(data, message));
}
