using AgriERP.Domain.Entities.System;

namespace AgriERP.Application.Features.Modules;

/// <summary>
/// Reads ModuleMaster.
///
/// A named repository rather than the generic <c>IRepository&lt;ModuleMaster&gt;</c>
/// because this table has exactly one query - the live menu, in display order -
/// and it must be identical for every caller. Leaving the WHERE and the ORDER BY
/// to each call site is how a retired entry eventually reappears in one of them.
///
/// Read-only on purpose: the menu is maintained by insert scripts, not by the
/// running application, and a repository that cannot write cannot be the thing
/// that reorders someone's sidebar mid-shift.
/// </summary>
public interface IModuleRepository
{
    /// <summary>
    /// Every entry with IsDeletedTransaction = 0, ordered by
    /// ModuleHeadDisplayOrder then ModuleDisplayOrder.
    /// </summary>
    Task<IReadOnlyList<ModuleMaster>> GetActiveModulesAsync(CancellationToken ct = default);
}
