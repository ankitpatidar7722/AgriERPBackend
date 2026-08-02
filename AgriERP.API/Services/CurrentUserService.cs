using AgriERP.Application.Common.Interfaces;
using AgriERP.Shared.Constants;
using System.Security.Claims;

namespace AgriERP.API.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public int? UserId =>
        int.TryParse(User?.FindFirstValue(AgriClaimTypes.UserId), out var id) ? id : null;

    public string? UserName => User?.FindFirstValue(ClaimTypes.Name);

    public string? FullName => User?.FindFirstValue(AgriClaimTypes.FullName);

    public string? RoleName => User?.FindFirstValue(ClaimTypes.Role);

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyCollection<string> Permissions =>
        User?.FindAll(AgriClaimTypes.Permission).Select(c => c.Value).ToArray() ?? Array.Empty<string>();

    /// <summary>
    /// Prefers the X-Forwarded-For header so a reverse proxy does not make
    /// every audit row read as the proxy's own address.
    /// </summary>
    public string? IpAddress
    {
        get
        {
            var context = _accessor.HttpContext;
            if (context is null) return null;

            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public bool HasPermission(string permission)
        => Permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
}
