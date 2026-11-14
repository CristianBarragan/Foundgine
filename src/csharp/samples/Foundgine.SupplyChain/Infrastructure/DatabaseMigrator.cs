using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Foundgine.SupplyChain.Infrastructure;

/// <summary>
/// Applies the EF Core migrations (<c>Migrations/</c>) at startup when <c>Database:MigrateOnStart</c> is true, then
/// seeds demo rows through the generic repositories when <c>Database:SeedDemoData</c> is true and the database is empty.
/// </summary>
public sealed class DatabaseMigrator(IDbContextFactory<FoundgineDbContext> factory, IServiceScopeFactory scopes,
    ILogger<DatabaseMigrator> log)
{
    public async Task MigrateAsync(bool seedDemoData, CancellationToken ct)
    {
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await db.Database.MigrateAsync(ct);
            log.LogInformation("Database is at migration {Migration}",
                (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault());
        }

        if (!seedDemoData) return;
        using var scope = scopes.CreateScope();
        await DemoData.SeedAsync(scope.ServiceProvider, ct);
    }
}
