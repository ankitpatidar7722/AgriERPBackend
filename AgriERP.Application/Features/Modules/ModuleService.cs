using AgriERP.Application.Common.Interfaces;
using AgriERP.Application.Features.Modules.Dtos;
using AgriERP.Domain.Entities.System;
using AgriERP.Shared.Constants;
using AutoMapper;

namespace AgriERP.Application.Features.Modules;

public interface IModuleService
{
    /// <summary>
    /// The sidebar for the signed-in user: live entries only, grouped by head,
    /// groups in ModuleHeadDisplayOrder and items in ModuleDisplayOrder.
    /// </summary>
    Task<IReadOnlyList<SidebarGroupDto>> GetSidebarAsync(CancellationToken ct = default);
}

public class ModuleService : IModuleService
{
    private readonly IModuleRepository _modules;
    private readonly ICurrentUserService _currentUser;
    private readonly IMapper _mapper;

    public ModuleService(
        IModuleRepository modules,
        ICurrentUserService currentUser,
        IMapper mapper)
    {
        _modules = modules;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    /// <summary>
    /// Route -> the permissions that make it worth showing, any one of which is
    /// enough.
    ///
    /// The MENU is data; this is not the menu. It is the same visibility rule
    /// the endpoints already enforce, applied one step earlier so a storekeeper
    /// is not offered a Customers link that answers 403. Dropping it would not
    /// leak anything - every controller still checks - but it would put dead
    /// entries in front of people, which teaches them the app is unreliable.
    ///
    /// A route ABSENT from this map is visible to every signed-in user. That is
    /// the deliberate default: a module inserted into ModuleMaster tomorrow
    /// appears in the sidebar with no code change, which is the whole point of
    /// the table. Gate it here only once its screen has a permission to gate.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> RoutePermissions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["/dashboard"]          = [Permissions.Dashboard.View],
            ["/dashboard/sales"]    = [Permissions.Dashboard.View],
            ["/dashboard/purchase"] = [Permissions.Dashboard.View],
            ["/sales"]      = [Permissions.Sales.View],
            ["/purchases"]  = [Permissions.Purchase.View],
            ["/payments"]   = [Permissions.Payment.View],
            ["/stock"]      = [Permissions.Stock.View],
            ["/reports"]    =
            [
                Permissions.Report.Stock,
                Permissions.Report.Sales,
                Permissions.Report.Purchase,
                Permissions.Report.Profit,
                Permissions.Report.Gst
            ],
            ["/items"]   = [Permissions.Item.View],
            ["/itemSubGroups"] = [Permissions.ItemSubGroup.View],
            ["/companies"]  = [Permissions.Company.View],
            ["/suppliers"]  = [Permissions.Supplier.View],
            ["/customers"]  = [Permissions.Customer.View],
            // Units are maintained under the item permissions - a unit only
            // exists to serve items, matching UnitsController.
            ["/units"]      = [Permissions.Item.View]
        };

    public async Task<IReadOnlyList<SidebarGroupDto>> GetSidebarAsync(CancellationToken ct = default)
    {
        var modules = await _modules.GetActiveModulesAsync(ct);

        return modules
            .Where(IsVisibleToCurrentUser)
            // The repository already returned them in order, and GroupBy
            // preserves first-appearance order for both the groups and their
            // contents - so no re-sort here, and the two orderings cannot drift
            // apart into two different answers.
            .GroupBy(m => m.ModuleHeadName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SidebarGroupDto
            {
                ModuleHeadName         = group.First().ModuleHeadName,
                ModuleHeadDisplayName  = group.First().ModuleHeadDisplayName,
                ModuleHeadDisplayOrder = group.First().ModuleHeadDisplayOrder,
                SetGroupIndex          = group.First().SetGroupIndex,
                Modules                = group.Select(_mapper.Map<ModuleDto>).ToList()
            })
            // A group whose every entry was filtered out would render as a bare
            // heading with nothing under it.
            .Where(group => group.Modules.Count > 0)
            .ToList();
    }

    private bool IsVisibleToCurrentUser(ModuleMaster module)
        => !RoutePermissions.TryGetValue(module.ModuleName, out var required)
           || required.Any(_currentUser.HasPermission);
}
