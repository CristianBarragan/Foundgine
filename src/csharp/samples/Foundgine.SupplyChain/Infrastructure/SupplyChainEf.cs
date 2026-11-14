using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Foundgine.SupplyChain.Infrastructure;

/// <summary>
/// The one place that configures EF Core for the starter: the Foundgine-derived model plus
/// <see cref="SupplyChainStorage"/>, Npgsql and the migrations assembly. Used by the running app and by the
/// design-time factory (<c>dotnet ef</c>).
/// </summary>
public static class SupplyChainEf
{
    public static void ConfigureModel(FoundgineEfOptions options) => options.Customize(SupplyChainStorage.Apply);

    public static DbContextOptionsBuilder UseSupplyChainNpgsql(this DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(SupplyChainEf).Assembly.GetName().Name))
            // The snapshot is generated from the same spec as the migration (tools/gen_migration.py); the diff
            // check is advisory until `dotnet ef migrations has-pending-model-changes` has been run once.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
}

/// <summary>Lets <c>dotnet ef migrations add/script</c> build the context without starting the web host.</summary>
public sealed class SupplyChainDesignTimeFactory : IDesignTimeDbContextFactory<FoundgineDbContext>
{
    public FoundgineDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(SupplyChainDomain.ConnectionStringName)
                         ?? SupplyChainDomain.DevConnectionString;
        var options = new DbContextOptionsBuilder<FoundgineDbContext>().UseSupplyChainNpgsql(connection).Options;
        var efOptions = new FoundgineEfOptions();
        SupplyChainEf.ConfigureModel(efOptions);
        return new FoundgineDbContext((DbContextOptions<FoundgineDbContext>)options, new FoundgineEfModel(SupplyChainDomain.Metadata, efOptions));
    }
}
