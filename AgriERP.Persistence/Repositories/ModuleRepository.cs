using AgriERP.Application.Features.Modules;
using AgriERP.Domain.Entities.System;
using AgriERP.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.Persistence.Repositories;

public class ModuleRepository : IModuleRepository
{
    private readonly AgriErpDbContext _context;

    public ModuleRepository(AgriErpDbContext context) => _context = context;

    public async Task<IReadOnlyList<ModuleMaster>> GetActiveModulesAsync(CancellationToken ct = default)
        => await _context.Modules
            .AsNoTracking()
            .Where(m => !m.IsDeletedTransaction)
            // ModuleHeadDisplayOrder first so groups keep their sequence, then
            // ModuleDisplayOrder within the group. ModuleId last as the
            // tie-break: two rows sharing a display order would otherwise swap
            // places between requests, and a menu that reshuffles itself is the
            // kind of bug nobody can reproduce.
            .OrderBy(m => m.ModuleHeadDisplayOrder)
            .ThenBy(m => m.ModuleDisplayOrder)
            .ThenBy(m => m.ModuleId)
            .ToListAsync(ct);
}
