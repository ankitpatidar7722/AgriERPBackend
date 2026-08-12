using System.Runtime.CompilerServices;

namespace AgriERP.Persistence.Tests;

internal static class TestModuleInit
{
    /// <summary>
    /// Production sets this in AddPersistence's Npgsql branch; the tests build
    /// their contexts directly, so set it here too - before any Npgsql data
    /// source is created - so test behaviour matches the deployed app (UTC
    /// DateTimes written to timestamp(3) columns).
    /// </summary>
    [ModuleInitializer]
    internal static void Init()
        => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
}
