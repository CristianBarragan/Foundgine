using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class SupplyChainDesignTimeFactory : IDesignTimeDbContextFactory<SupplyChainDbContext>
{
    public SupplyChainDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__SupplyChain")
            ?? "Host=localhost;Database=sc_cs_baseline;Username=benchmark;Password=benchmark";
        var options = new DbContextOptionsBuilder<SupplyChainDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(SupplyChainDbContext).Assembly.GetName().Name))
            .Options;
        return new SupplyChainDbContext(options);
    }
}
