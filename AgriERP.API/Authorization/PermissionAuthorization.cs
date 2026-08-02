using AgriERP.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AgriERP.API.Authorization;

/// <summary>
/// Requires a specific permission code, e.g. [HasPermission(Permissions.Item.Create)].
///
/// Permission-based rather than role-based: roles change as a shop grows
/// ("Manager can now cancel bills"), and role checks scattered across
/// controllers have to be hunted down every time. A permission code is a
/// stable contract; which roles hold it is a row in RolePermissions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "PERMISSION:";

    public HasPermissionAttribute(string permission) => Policy = PolicyPrefix + permission;
}

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

/// <summary>
/// Builds a policy on demand for any "PERMISSION:xxx" name, so adding an
/// endpoint never means registering another policy in Program.cs.
/// </summary>
public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
            return await base.GetPolicyAsync(policyName);

        var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // Read straight from the token's claims. Checking the database on every
        // request would add a round trip to every single call; the cost of the
        // claims approach is that a revoked permission survives until the
        // short-lived access token expires.
        var hasPermission = context.User.Claims.Any(c =>
            c.Type == AgriClaimTypes.Permission &&
            string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase));

        if (hasPermission)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
