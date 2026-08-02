using AgriERP.Application.Common.Exceptions;
using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Auth.Dtos;
using AgriERP.Domain.Entities.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace AgriERP.Application.Features.Auth;

public class AuthService : IAuthService
{
    // Kept generic on purpose. "No such user" versus "wrong password" tells an
    // attacker which usernames exist, and a village shop's usernames are
    // guessable.
    private const string InvalidCredentials = "Invalid username or password.";

    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUnitOfWork uow,
        IPasswordHasher passwordHasher,
        IJwtTokenService tokenService,
        IDateTimeProvider clock,
        AuthOptions options,
        ILogger<AuthService> logger)
    {
        _uow = uow;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public async Task<AuthResponse> LoginAsync(
        LoginRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var normalized = request.UserName.Trim().ToUpperInvariant();

        var user = await _uow.Repository<User>()
            .Query(tracking: true)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(
                u => (u.NormalizedUserName == normalized || u.NormalizedEmail == normalized)
                     && !u.IsDeleted,
                ct);

        if (user is null)
        {
            // Hash anyway so a missing user does not return measurably faster
            // than a wrong password.
            _passwordHasher.Verify(request.Password, _options.DummyHash);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!user.IsActive)
            throw new UnauthorizedException("This account has been deactivated. Contact your administrator.");

        if (user.LockoutEndAt is { } lockoutEnd && lockoutEnd > _clock.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((lockoutEnd - _clock.UtcNow).TotalMinutes));
            throw new UnauthorizedException(
                $"Account locked after too many failed attempts. Try again in {minutes} minute(s).");
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.AccessFailedCount++;

            if (user.AccessFailedCount >= _options.MaxFailedAttempts)
            {
                user.LockoutEndAt = _clock.UtcNow.AddMinutes(_options.LockoutMinutes);
                user.AccessFailedCount = 0;
                _logger.LogWarning("User {UserName} locked out until {LockoutEnd} from {Ip}",
                    user.UserName, user.LockoutEndAt, ipAddress);
            }

            await _uow.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = _clock.UtcNow;

        var permissions = await GetPermissionsAsync(user.RoleId, ct);
        var tokens = _tokenService.CreateTokens(user, permissions);

        await _uow.Repository<UserRefreshToken>().AddAsync(new UserRefreshToken
        {
            UserId = user.UserId,
            TokenHash = _tokenService.HashRefreshToken(tokens.RefreshToken),
            ExpiresAt = tokens.RefreshTokenExpiresAt,
            CreatedAt = _clock.UtcNow,
            CreatedByIp = ipAddress,
            UserAgent = Truncate(userAgent, 300)
        }, ct);

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserName} signed in from {Ip}", user.UserName, ipAddress);

        return BuildResponse(user, permissions, tokens);
    }

    public async Task<AuthResponse> RefreshTokenAsync(
        string refreshToken, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var hash = _tokenService.HashRefreshToken(refreshToken);

        var stored = await _uow.Repository<UserRefreshToken>()
            .Query(tracking: true)
            .Include(t => t.User)
                .ThenInclude(u => u!.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored?.User is null)
            throw new UnauthorizedException("Invalid refresh token.");

        // Reuse of an already-rotated token means the token was captured. The
        // legitimate holder and the attacker both have it, and there is no way
        // to tell which is calling - so revoke the entire chain and force a
        // fresh login.
        if (stored.RevokedAt is not null)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId} from {Ip}. Revoking all sessions.",
                stored.UserId, ipAddress);

            await RevokeAllTokensForUserAsync(stored.UserId, ipAddress, ct);
            await _uow.SaveChangesAsync(ct);

            throw new UnauthorizedException("Session is no longer valid. Please sign in again.");
        }

        if (stored.ExpiresAt <= _clock.UtcNow)
            throw new UnauthorizedException("Session has expired. Please sign in again.");

        var user = stored.User;

        if (!user.IsActive || user.IsDeleted)
            throw new UnauthorizedException("This account is no longer active.");

        var permissions = await GetPermissionsAsync(user.RoleId, ct);
        var tokens = _tokenService.CreateTokens(user, permissions);

        var replacement = new UserRefreshToken
        {
            UserId = user.UserId,
            TokenHash = _tokenService.HashRefreshToken(tokens.RefreshToken),
            ExpiresAt = tokens.RefreshTokenExpiresAt,
            CreatedAt = _clock.UtcNow,
            CreatedByIp = ipAddress,
            UserAgent = Truncate(userAgent, 300)
        };

        await _uow.Repository<UserRefreshToken>().AddAsync(replacement, ct);
        await _uow.SaveChangesAsync(ct);   // needed so the replacement has an id

        stored.RevokedAt = _clock.UtcNow;
        stored.RevokedByIp = ipAddress;
        stored.ReplacedByTokenId = replacement.RefreshTokenId;
        await _uow.SaveChangesAsync(ct);

        return BuildResponse(user, permissions, tokens);
    }

    public async Task LogoutAsync(string refreshToken, string? ipAddress, CancellationToken ct = default)
    {
        var hash = _tokenService.HashRefreshToken(refreshToken);

        var stored = await _uow.Repository<UserRefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, tracking: true, ct);

        // Logging out with an unknown or already-revoked token is not an error:
        // the caller's intent is "end my session", and it already has.
        if (stored is null || stored.RevokedAt is not null)
            return;

        stored.RevokedAt = _clock.UtcNow;
        stored.RevokedByIp = ipAddress;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        if (request.NewPassword != request.ConfirmPassword)
            throw new ValidationException(nameof(request.ConfirmPassword), "Passwords do not match.");

        var user = await _uow.Repository<User>()
            .FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted, tracking: true, ct)
            ?? throw new NotFoundException("User", userId);

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ValidationException(nameof(request.CurrentPassword), "Current password is incorrect.");

        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            throw new ValidationException(nameof(request.NewPassword), "New password must differ from the current one.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.LastPasswordChangeAt = _clock.UtcNow;
        user.MustChangePassword = false;
        // Rotating the stamp is what makes already-issued access tokens stale
        // on their next refresh.
        user.SecurityStamp = Guid.NewGuid();

        // A password change must end every other session - that is the whole
        // point of changing it after a suspected compromise.
        await RevokeAllTokensForUserAsync(userId, null, ct);

        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Password changed for user {UserId}", userId);
    }

    public async Task<string?> ForgotPasswordAsync(
        ForgotPasswordRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var normalized = request.UserNameOrEmail.Trim().ToUpperInvariant();

        var user = await _uow.Repository<User>()
            .FirstOrDefaultAsync(
                u => (u.NormalizedUserName == normalized || u.NormalizedEmail == normalized)
                     && !u.IsDeleted && u.IsActive,
                tracking: false, ct);

        // Returns null rather than 404 for an unknown account: a different
        // response here would turn this endpoint into a username oracle.
        if (user is null)
        {
            _logger.LogInformation("Password reset requested for unknown account from {Ip}", ipAddress);
            return null;
        }

        var token = GenerateSecureToken();

        await _uow.Repository<UserPasswordReset>().AddAsync(new UserPasswordReset
        {
            UserId = user.UserId,
            TokenHash = _tokenService.HashRefreshToken(token),
            ExpiresAt = _clock.UtcNow.AddMinutes(_options.PasswordResetMinutes),
            RequestedAt = _clock.UtcNow,
            RequestedByIp = ipAddress
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return token;
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        if (request.NewPassword != request.ConfirmPassword)
            throw new ValidationException(nameof(request.ConfirmPassword), "Passwords do not match.");

        var hash = _tokenService.HashRefreshToken(request.Token);

        var reset = await _uow.Repository<UserPasswordReset>()
            .Query(tracking: true)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == hash, ct);

        if (reset?.User is null || reset.UsedAt is not null || reset.ExpiresAt <= _clock.UtcNow)
            throw new ValidationException(nameof(request.Token), "This reset link is invalid or has expired.");

        reset.User.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        reset.User.LastPasswordChangeAt = _clock.UtcNow;
        reset.User.MustChangePassword = false;
        reset.User.SecurityStamp = Guid.NewGuid();
        reset.User.AccessFailedCount = 0;
        reset.User.LockoutEndAt = null;

        // Single use, or a leaked link stays valid until it expires.
        reset.UsedAt = _clock.UtcNow;

        await RevokeAllTokensForUserAsync(reset.UserId, null, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Password reset completed for user {UserId}", reset.UserId);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(int userId, CancellationToken ct = default)
    {
        var user = await _uow.Repository<User>()
            .Query()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted, ct)
            ?? throw new NotFoundException("User", userId);

        var permissions = await GetPermissionsAsync(user.RoleId, ct);
        return MapUser(user, permissions);
    }

    // ---- helpers ------------------------------------------------------------

    private async Task<IReadOnlyCollection<string>> GetPermissionsAsync(int roleId, CancellationToken ct)
        => await _uow.Repository<RolePermission>()
            .Query()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission!.PermissionCode)
            .ToListAsync(ct);

    private async Task RevokeAllTokensForUserAsync(int userId, string? ipAddress, CancellationToken ct)
    {
        var active = await _uow.Repository<UserRefreshToken>()
            .Query(tracking: true)
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.RevokedAt = _clock.UtcNow;
            token.RevokedByIp = ipAddress;
        }
    }

    private AuthResponse BuildResponse(User user, IReadOnlyCollection<string> permissions, TokenPair tokens)
        => new()
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt,
            User = MapUser(user, permissions)
        };

    private static CurrentUserDto MapUser(User user, IReadOnlyCollection<string> permissions)
        => new()
        {
            UserId = user.UserId,
            UserName = user.UserName,
            FullName = user.FullName,
            Email = user.Email,
            Mobile = user.Mobile,
            RoleName = user.Role?.RoleName ?? string.Empty,
            AvatarPath = user.AvatarPath,
            MustChangePassword = user.MustChangePassword,
            LastLoginAt = user.LastLoginAt,
            Permissions = permissions
        };

    private static string GenerateSecureToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                  .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// Auth policy, bound from the JwtSettings section of appsettings.json.
/// Security configuration lives in one place - not split between config and
/// the database, where one copy is eventually wrong and nobody knows which is
/// in force.
/// </summary>
public class AuthOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int PasswordResetMinutes { get; set; } = 30;

    /// <summary>
    /// A real BCrypt hash of a throwaway value, used to burn the same CPU time
    /// on an unknown username as on a wrong password. Without it, login timing
    /// reveals which usernames exist.
    /// </summary>
    public string DummyHash { get; set; } =
        "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";
}
