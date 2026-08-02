using AgriERP.Application.Features.Auth.Dtos;

namespace AgriERP.Application.Features.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default);

    Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress, string? userAgent, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, string? ipAddress, CancellationToken ct = default);

    Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns the reset token rather than emailing it. Wiring an SMTP sender
    /// is an Infrastructure concern for a later step; today the token is handed
    /// to an administrator to pass on.
    /// </summary>
    Task<string?> ForgotPasswordAsync(ForgotPasswordRequest request, string? ipAddress, CancellationToken ct = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    Task<CurrentUserDto> GetCurrentUserAsync(int userId, CancellationToken ct = default);
}
