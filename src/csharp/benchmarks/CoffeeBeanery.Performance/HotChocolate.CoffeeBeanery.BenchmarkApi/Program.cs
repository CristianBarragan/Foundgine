using CoffeeBeanery.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString =
    builder.Configuration.GetConnectionString("BankingConnectionString")
    ?? throw new InvalidOperationException(
        "Connection string 'BankingConnectionString' is not configured.");

builder.Services.AddDbContext<BankingEntityContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddGraphQLServer()
    .ModifyRequestOptions(options => options.IncludeExceptionDetails = true)
    .AddProjections()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

var app = builder.Build();
app.MapGraphQL("/graphql");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (BankingEntityContext db, CancellationToken cancellationToken) =>
{
    try
    {
        await db.Database.CanConnectAsync(cancellationToken);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Database is not ready.", detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.Run();

public sealed class Query
{
    public async Task<IReadOnlyList<CustomerGraph>> GetCustomer(
        int? first, BankingEntityContext db, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(first ?? 50, 1, 50);
        var customers = await db.Customer.AsNoTracking()
            .OrderBy(x => x.Id).Take(take)
            .Include(x => x.CustomerBankingRelationship!)
            .ThenInclude(x => x.Contract!)
            .ThenInclude(x => x.Transaction!)
            .ToListAsync(cancellationToken);
        return customers.Select(CustomerGraph.From).ToArray();
    }
}

public sealed class Mutation
{
    public async Task<CustomerGraph> CreateCustomer(CreateCustomerInput input, BankingEntityContext db,
        CancellationToken cancellationToken)
    {
        var customer = new Customer
        {
            CustomerKey = input.CustomerKey, FirstName = input.FirstName,
            LastName = input.LastName, FullName = input.FullName, CustomerType = input.CustomerType
        };
        foreach (var ri in input.CustomerBankingRelationship ?? [])
        {
            var relationship = new CustomerBankingRelationship
            {
                CustomerBankingRelationshipKey = ri.CustomerBankingRelationshipKey,
                Customer = customer, CustomerKey = input.CustomerKey
            };
            foreach (var ci in ri.Contract ?? [])
            {
                var contract = new Contract
                {
                    ContractKey = ci.ContractKey, ContractType = ci.ContractType,
                    Amount = ci.Amount, CustomerBankingRelationship = relationship
                };
                foreach (var ti in ci.Transaction ?? [])
                    contract.Transaction!.Add(new Transaction
                    {
                        TransactionKey = ti.TransactionKey,
                        Amount = ti.Amount, Balance = ti.Balance, Contract = contract, ContractKey = ci.ContractKey
                    });
                relationship.Contract!.Add(contract);
            }

            customer.CustomerBankingRelationship!.Add(relationship);
        }

        db.Customer.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return CustomerGraph.From(customer);
    }

    public async Task<Customer> UpsertCustomer(CustomerInput input, string[]? onConflict, BankingEntityContext db,
        CancellationToken cancellationToken)
    {
        if (input.CustomerKey is null)
            throw new HotChocolate.GraphQLException("The benchmark upsert requires CustomerKey.");

        if (onConflict is null || onConflict.Length != 1 ||
            !string.Equals(onConflict[0], "CustomerKey", StringComparison.OrdinalIgnoreCase))
        {
            throw new HotChocolate.GraphQLException("The benchmark upsert requires onConflict: [\"CustomerKey\"].");
        }

        // The benchmark intentionally performs a real PostgreSQL upsert against
        // an existing deterministic customer. The subsequent load-test query is
        // the exact same top-50/full-graph query used by the standalone read
        // workload, so the combined operation is a write-then-refetch measurement.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
                                                       INSERT INTO "Banking"."Customer"
                                                           ("CustomerKey", "FirstName", "LastName", "FullName")
                                                       VALUES ({input.CustomerKey.Value}, {input.FirstName}, {input.LastName}, {input.FullName})
                                                       ON CONFLICT ("CustomerKey") DO UPDATE SET
                                                           "FirstName" = EXCLUDED."FirstName",
                                                           "LastName" = EXCLUDED."LastName",
                                                           "FullName" = EXCLUDED."FullName";
                                                       """, cancellationToken);

        return await db.Customer.AsNoTracking()
            .SingleAsync(x => x.CustomerKey == input.CustomerKey.Value, cancellationToken);
    }

    public async Task<Customer> UpdateCustomer(CustomerInput input, CustomerWhereInput where, BankingEntityContext db,
        CancellationToken cancellationToken)
    {
        var entity = await db.Customer.SingleAsync(x => x.Id == where.Id.Eq, cancellationToken);
        entity.FirstName = input.FirstName;
        entity.LastName = input.LastName;
        entity.FullName = input.FullName;
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<CustomerBankingRelationship> UpdateCustomerBankingRelationship(
        CustomerBankingRelationshipInput input, CustomerBankingRelationshipWhereInput where, BankingEntityContext db,
        CancellationToken cancellationToken)
    {
        var entity = await db.CustomerBankingRelationship.SingleAsync(x => x.Id == where.Id.Eq, cancellationToken);
        entity.CustomerBankingRelationshipKey = input.CustomerBankingRelationshipKey;
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Contract> UpdateContract(ContractInput input, ContractWhereInput where, BankingEntityContext db,
        CancellationToken cancellationToken)
    {
        var entity = await db.Contract.SingleAsync(x => x.Id == where.Id.Eq, cancellationToken);
        entity.Amount = input.Amount;
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<Transaction> UpdateTransaction(TransactionInput input, TransactionWhereInput where,
        BankingEntityContext db, CancellationToken cancellationToken)
    {
        var entity = await db.Transaction.SingleAsync(x => x.Id == where.Id.Eq, cancellationToken);
        entity.Amount = input.Amount;
        entity.Balance = input.Balance;
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }
}

public sealed record CreateCustomerInput(
    Guid CustomerKey,
    string? FirstName,
    string? LastName,
    string? FullName,
    CustomerType? CustomerType,
    IReadOnlyList<CreateCustomerBankingRelationshipInput>? CustomerBankingRelationship);

public sealed record CreateCustomerBankingRelationshipInput(
    Guid CustomerBankingRelationshipKey,
    IReadOnlyList<CreateContractInput>? Contract);

public sealed record CreateContractInput(
    Guid ContractKey,
    ContractType? ContractType,
    decimal? Amount,
    IReadOnlyList<CreateTransactionInput>? Transaction);

public sealed record CreateTransactionInput(Guid TransactionKey, decimal? Amount, decimal? Balance);

public sealed record CustomerInput(Guid? CustomerKey, string? FirstName, string? LastName, string? FullName);

public sealed record CustomerBankingRelationshipInput(Guid CustomerBankingRelationshipKey);

public sealed record ContractInput(decimal Amount);

public sealed record TransactionInput(decimal Amount, decimal Balance);

public sealed record CustomerWhereInput(IdWhere Id);

public sealed record CustomerBankingRelationshipWhereInput(IdWhere Id);

public sealed record ContractWhereInput(IdWhere Id);

public sealed record TransactionWhereInput(IdWhere Id);

public sealed record IdWhere(int Eq);

public sealed record CustomerGraph(
    int Id,
    Guid CustomerKey,
    string? FirstName,
    string? LastName,
    string? FullName,
    IReadOnlyList<CustomerBankingRelationshipGraph> CustomerBankingRelationship)
{
    public static CustomerGraph From(Customer x) => new(x.Id, x.CustomerKey, x.FirstName, x.LastName, x.FullName,
        (x.CustomerBankingRelationship ?? []).Select(CustomerBankingRelationshipGraph.From).ToArray());
}

public sealed record CustomerBankingRelationshipGraph(
    int Id,
    Guid CustomerBankingRelationshipKey,
    IReadOnlyList<ContractGraph> Contract)
{
    public static CustomerBankingRelationshipGraph From(CustomerBankingRelationship x) => new(x.Id,
        x.CustomerBankingRelationshipKey,
        (x.Contract ?? []).Select(ContractGraph.From).ToArray());
}

public sealed record ContractGraph(
    int Id,
    Guid ContractKey,
    decimal? Amount,
    IReadOnlyList<TransactionGraph> Transaction)
{
    public static ContractGraph From(Contract x) => new(x.Id, x.ContractKey, x.Amount,
        (x.Transaction ?? []).Select(TransactionGraph.From).ToArray());
}

public sealed record TransactionGraph(int Id, Guid TransactionKey, decimal? Amount, decimal? Balance)
{
    public static TransactionGraph From(Transaction x) => new(x.Id, x.TransactionKey, x.Amount, x.Balance);
}