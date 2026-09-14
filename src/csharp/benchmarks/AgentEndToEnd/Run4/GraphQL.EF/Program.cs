using CoffeeBeanery.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("BankingConnectionString") ??
         throw new InvalidOperationException("Missing connection string.");
builder.Services.AddPooledDbContextFactory<BankingEntityContext>(o => o.UseNpgsql(cs));
builder.Services.AddGraphQLServer().AddQueryType<Query>();
var app = builder.Build();
app.MapGraphQL("/graphql");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (IDbContextFactory<BankingEntityContext> factory, CancellationToken ct) =>
{
    await using var db = await factory.CreateDbContextAsync(ct);
    return await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
});
app.Run();

public sealed class Query
{
    private readonly IDbContextFactory<BankingEntityContext> _factory;
    public Query(IDbContextFactory<BankingEntityContext> factory) => _factory = factory;

    public async Task<CustomerDto?> Customer(int id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var x = await db.Customer.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        return x is null ? null : new(x.Id, x.CustomerKey, x.FullName);
    }

    public async Task<IReadOnlyList<RelationshipDto>> Relationships(int customerId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.CustomerBankingRelationship.AsNoTracking().Where(x => x.CustomerId == customerId)
            .Select(x => new RelationshipDto(x.Id, x.CustomerBankingRelationshipKey)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ContractDto>> Contracts(int customerId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Contract.AsNoTracking().Where(x => x.CustomerBankingRelationship!.CustomerId == customerId)
            .Select(x => new ContractDto(x.Id, x.ContractKey, x.Amount)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TransactionDto>> Transactions(int customerId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Transaction.AsNoTracking()
            .Where(x => x.Contract!.CustomerBankingRelationship!.CustomerId == customerId)
            .Select(x => new TransactionDto(x.Id, x.TransactionKey, x.Amount, x.Balance)).ToListAsync(ct);
    }

    public async Task<ExposureDto> Exposure(int customerId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var contracts = await db.Contract.AsNoTracking()
            .Where(x => x.CustomerBankingRelationship!.CustomerId == customerId)
            .Select(x => x.Amount ?? 0m).ToListAsync(ct);
        var balances = await db.Transaction.AsNoTracking()
            .Where(x => x.Contract!.CustomerBankingRelationship!.CustomerId == customerId)
            .Select(x => x.Balance ?? 0m).ToListAsync(ct);
        return new(contracts.Count, contracts.Sum(), balances.Sum());
    }

    public async Task<CustomerDto?> CustomerVerify(int id, CancellationToken ct) => await Customer(id, ct);
}

public record CustomerDto(int Id, Guid CustomerKey, string? FullName);

public record RelationshipDto(int Id, Guid RelationshipKey);

public record ContractDto(int Id, Guid ContractKey, decimal? Amount);

public record TransactionDto(int Id, Guid TransactionKey, decimal? Amount, decimal? Balance);

public record ExposureDto(int ContractCount, decimal ContractAmount, decimal TransactionBalance);