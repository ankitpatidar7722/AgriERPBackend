using AgriERP.Application.Features.Auth;
using AgriERP.Application.Features.Auth.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriERP.API.Controllers;

[AllowAnonymous]
public class AuthController : BaseApiController
{
    private readonly IAuthService _auth;
    private readonly IHostEnvironment _environment;

    public AuthController(IAuthService auth, IHostEnvironment environment)
    {
        _auth = auth;
        _environment = environment;
    }

    /// <summary>Signs in and returns an access token, a refresh token and the user's permissions.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
        => Success(await _auth.LoginAsync(request, IpAddress, UserAgent, ct), "Signed in.");

    /// <summary>Exchanges a refresh token for a new pair. The old token is revoked.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken ct)
        => Success(await _auth.RefreshTokenAsync(request.RefreshToken, IpAddress, UserAgent, ct));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request.RefreshToken, IpAddress, ct);
        return Success("Signed out.");
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
        => Success(await _auth.GetCurrentUserAsync(CurrentUserId, ct));

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await _auth.ChangePasswordAsync(CurrentUserId, request, ct);
        return Success("Password changed. Please sign in again on your other devices.");
    }

    /// <summary>
    /// Always reports success, whether or not the account exists - a different
    /// response for an unknown username would turn this into a way to discover
    /// who has a login.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var token = await _auth.ForgotPasswordAsync(request, IpAddress, ct);

        // Until an email sender exists, Development returns the token so the
        // flow is testable. It is never returned outside Development, where it
        // would be a password-reset bypass for anyone who can guess a username.
        if (_environment.IsDevelopment() && token is not null)
            return Success(new { resetToken = token },
                "Development only: this token is returned here because email delivery is not wired up yet.");

        return Success("If that account exists, a reset link has been issued.");
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request, ct);
        return Success("Password reset. You can now sign in.");
    }
}
