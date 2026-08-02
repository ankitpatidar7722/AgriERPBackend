using AgriERP.Domain.Entities.Security;

namespace AgriERP.Application.Common.Interfaces;

/// <summary>Who is making the current request. Implemented in the API from the JWT.</summary>
public interface ICurrentUserService
{
    int? UserId { get; }
    string? UserName { get; }
    string? FullName { get; }
    string? RoleName { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Permissions { get; }
    string? IpAddress { get; }
    bool HasPermission(string permission);
}

/// <summary>
/// Abstracts DateTime.UtcNow so time-dependent logic (token expiry, near-expiry
/// windows, due dates) can be tested without waiting for the clock.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }

    /// <summary>Server-local calendar date. Business dates are local, audit stamps are UTC.</summary>
    DateTime Today { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>False for any malformed or sentinel hash rather than throwing.</summary>
    bool Verify(string password, string hash);
}

public record TokenPair(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt, DateTime RefreshTokenExpiresAt);

public interface IJwtTokenService
{
    TokenPair CreateTokens(User user, IReadOnlyCollection<string> permissions);

    /// <summary>SHA-256 of the opaque refresh token; only the hash is stored.</summary>
    byte[] HashRefreshToken(string refreshToken);
}

/// <summary>
/// Wraps usp_GetNextDocumentNumber.
///
/// Never derive the next number with MAX()+1 in C#: two salesmen pressing Save
/// at the same instant would both read the same value. The procedure does the
/// read-and-increment in one atomic UPDATE so the engine serialises them.
/// </summary>
public interface IDocumentNumberService
{
    Task<string> NextAsync(string documentType, CancellationToken ct = default);

    /// <summary>
    /// The number a document of this type WOULD get if saved now, without
    /// consuming the series. For showing an indicative number on a create form;
    /// the value actually assigned on save is authoritative and may differ if
    /// another document of the same type is saved first.
    /// </summary>
    Task<string?> PeekNextAsync(string documentType, CancellationToken ct = default);
}
