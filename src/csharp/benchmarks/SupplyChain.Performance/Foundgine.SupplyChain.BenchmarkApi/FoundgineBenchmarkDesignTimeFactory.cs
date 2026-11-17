using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain;
using Foundgine.SupplyChain.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Foundgine.SupplyChain.BenchmarkApi;

/// <summary>Creates the Foundgine benchmark's own context for its independent migration assembly.</summary>
public sealed class FoundgineBenchmarkDesignTimeFactory : IDesignTimeDbContextFactory<FoundgineDbContext>
{
    public FoundgineDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(SupplyChainDomain.ConnectionStringName)
            ?? "Host=localhost;Port=5432;Database=foundgine_benchmark;Username=benchmark;Password=change-me";
        var dbOptions = new DbContextOptionsBuilder<FoundgineDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly("Foundgine.SupplyChain.BenchmarkApi"))
            .Options;
        var efOptions = new FoundgineEfOptions();
        SupplyChainEf.ConfigureModel(efOptions);
        var model = new FoundgineEfModel(SupplyChainDomain.Metadata, efOptions);
        return new FoundgineDbContext(dbOptions, model);
    }
}
