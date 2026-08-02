using AgriERP.Application.Common.Interfaces;
using AgriERP.Domain.Entities.Security;
using AgriERP.Shared.Constants;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AgriERP.Infrastructure.Security;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "AgriERP";
    public string Audience { get; set; } = "AgriERP.Client";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 7;
}

public class JwtTokenService : IJwtTokenService
{
    /// <summary>
    /// HMAC-SHA256 needs at least 256 bits of key. A shorter key is not a
    /// weaker token, it is a startup failure - and better to fail at startup
    /// than to ship an installation signing with 8 characters.
    /// </summary>
    private const int MinimumKeyBytes = 32;

    private readonly JwtSettings _settings;
    private readonly IDateTimeProvider _clock;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtSettings> settings, IDateTimeProvider clock)
    {
        _settings = settings.Value;
        _clock = clock;

        if (string.IsNullOrWhiteSpace(_settings.Key))
            throw new InvalidOperationException(
                "JwtSettings:Key is not configured. Set it in appsettings.json or the environment.");

        var keyBytes = Encoding.UTF8.GetBytes(_settings.Key);

        if (keyBytes.Length < MinimumKeyBytes)
            throw new InvalidOperationException(
                $"JwtSettings:Key must be at least {MinimumKeyBytes} characters for HMAC-SHA256. " +
                $"It is currently {keyBytes.Length}.");

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    public TokenPair CreateTokens(User user, IReadOnlyCollection<string> permissions)
    {
        var issuedAt = _clock.UtcNow;
        var accessExpires = issuedAt.AddMinutes(_settings.AccessTokenMinutes);
        var refreshExpires = issuedAt.AddDays(_settings.RefreshTokenDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AgriClaimTypes.UserId, user.UserId.ToString()),
            new(AgriClaimTypes.FullName, user.FullName),
            new(ClaimTypes.Name, user.UserName),
            new(AgriClaimTypes.SecurityStamp, user.SecurityStamp.ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        if (user.Role is not null)
            claims.Add(new Claim(ClaimTypes.Role, user.Role.RoleName));

        // Permissions ride in the token so authorisation costs no database
        // round trip per request. The trade-off is that a permission revoked
        // mid-session stays effective until the access token expires - which
        // is why the access token is short-lived and a role change rotates the
        // security stamp, killing the session at the next refresh.
        foreach (var permission in permissions)
            claims.Add(new Claim(AgriClaimTypes.Permission, permission));

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: accessExpires,
            signingCredentials: _credentials);

        return new TokenPair(
            new JwtSecurityTokenHandler().WriteToken(token),
            GenerateRefreshToken(),
            accessExpires,
            refreshExpires);
    }

    public byte[] HashRefreshToken(string refreshToken)
        => SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));

    private static string GenerateRefreshToken()
        // 64 bytes from a cryptographic RNG. URL-safe so it survives being put
        // in a query string or a cookie without re-encoding.
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
                  .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
