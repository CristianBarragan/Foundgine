using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoffeeBeanery.Database;

public sealed class BankingEntityContextFactory : IDesignTimeDbContextFactory<BankingEntityContext>
{
    public BankingEntityContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__BankingConnectionString")
            ?? Environment.GetEnvironmentVariable("BankingConnectionString")
            ?? Environment.GetEnvironmentVariable("COFFEEBEANERY_CONNECTION")
            ?? "Host=localhost;Port=55432;Database=foundgine_benchmark;Username=benchmark;Password=benchmark";

        var options = new DbContextOptionsBuilder<BankingEntityContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BankingEntityContext(options);
    }
}
