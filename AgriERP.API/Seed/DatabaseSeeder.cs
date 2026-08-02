using AgriERP.Application.Common.Interfaces;
using AgriERP.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace AgriERP.API.Seed;

/// <summary>
/// Completes what 12_SeedData.sql deliberately could not: the administrator's
/// password hash.
///
/// The SQL script seeds the admin row with the sentinel '!SEED-PENDING!'
/// because a real hash committed to a .sql file is a live credential sitting
/// in source control. The hash is produced here, at first run, on the machine
/// that will actually run the shop.
/// </summary>
public static class DatabaseSeeder
{
    private const string SentinelHash = "!SEED-PENDING!";
    private const string DefaultAdminPassword = "Admin@123";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AgriErpDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                          .CreateLogger(typeof(DatabaseSeeder));

        if (!await context.Database.CanConnectAsync(ct))
            throw new InvalidOperationException(
                "Cannot reach the AgriERP database. Check ConnectionStrings:AgriERP and that SQL Server is running.");

        // Fails loudly rather than limping along: without the reference data
        // from 12_SeedData.sql, every item save would fail on a missing GST
        // slab and the cause would be far from obvious.
        if (!await context.Roles.AnyAsync(ct))
            throw new InvalidOperationException(
                "The database has no roles. Run database/scripts/ 00-12 before starting the API.");

        var admin = await context.Users.FirstOrDefaultAsync(u => u.UserName == "admin", ct);

        if (admin is null)
        {
            logger.LogWarning("No 'admin' user found. Run 12_SeedData.sql to create it.");
            return;
        }

        if (admin.PasswordHash != SentinelHash)
            return;   // already set on a previous run; never overwrite a real password

        admin.PasswordHash = hasher.Hash(DefaultAdminPassword);
        // Forces the change-password screen before anything else is reachable,
        // so the documented default cannot survive into day-to-day use.
        admin.MustChangePassword = true;
        admin.SecurityStamp = Guid.NewGuid();
        admin.LastPasswordChangeAt = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);

        logger.LogWarning(
            "Administrator password initialised to the default '{Password}'. " +
            "You will be required to change it at first login.",
            DefaultAdminPassword);
    }
}
