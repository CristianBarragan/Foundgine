using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoffeeBeanery.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var connectionString =
    Environment.GetEnvironmentVariable("BankingConnectionString")
    ?? Environment.GetEnvironmentVariable("COFFEEBEANERY_CONNECTION")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__BankingConnectionString")
    ?? throw new InvalidOperationException(
        "Connection string is not configured. Set " +
        "BankingConnectionString, COFFEEBEANERY_CONNECTION, " +
        "or ConnectionStrings__BankingConnectionString.");

var options = new DbContextOptionsBuilder<BankingEntityContext>()
    .UseNpgsql(connectionString)
    .ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
    .Options;

await using var db = new BankingEntityContext(options);

// The database service owns schema initialization and benchmark fixture seeding.
// Migrations are applied from the application so the container does not depend
// on dotnet-ef being available at runtime.
await db.Database.MigrateAsync();

var customerCount = GetInt("COFFEEBEANERY_CUSTOMERS", 1000);
var relationshipsPerCustomer = GetInt(
    "COFFEEBEANERY_RELATIONSHIPS_PER_CUSTOMER", 4);
var contractsPerRelationship = GetInt(
    "COFFEEBEANERY_CONTRACTS_PER_RELATIONSHIP", 3);
var transactionsPerContract = GetInt(
    "COFFEEBEANERY_TRANSACTIONS_PER_CONTRACT", 4);

var expectedRelationships = customerCount * relationshipsPerCustomer;
var expectedContracts = expectedRelationships * contractsPerRelationship;
var expectedTransactions = expectedContracts * transactionsPerContract;

var existingCounts = await GetCountsAsync(db);

if (existingCounts.Customers == customerCount &&
    existingCounts.Relationships == expectedRelationships &&
    existingCounts.Contracts == expectedContracts &&
    existingCounts.Transactions == expectedTransactions)
{
    await ValidateBenchmarkFixtureAsync(
        db,
        relationshipsPerCustomer,
        contractsPerRelationship,
        transactionsPerContract);

    Console.WriteLine(
        $"Database already contains the exact benchmark fixture: " +
        $"{customerCount:N0} customers, " +
        $"{expectedRelationships:N0} relationships, " +
        $"{expectedContracts:N0} contracts, " +
        $"{expectedTransactions:N0} transactions.");

    Console.WriteLine("Customer 1 is the deterministic benchmark target.");
    return;
}

if (existingCounts.Customers != 0 ||
    existingCounts.Relationships != 0 ||
    existingCounts.Contracts != 0 ||
    existingCounts.Transactions != 0)
{
    throw new InvalidOperationException(
        "Database contains a partial or incompatible benchmark fixture. " +
        $"Found customers={existingCounts.Customers:N0}, " +
        $"relationships={existingCounts.Relationships:N0}, " +
        $"contracts={existingCounts.Contracts:N0}, " +
        $"transactions={existingCounts.Transactions:N0}; " +
        $"expected customers={customerCount:N0}, " +
        $"relationships={expectedRelationships:N0}, " +
        $"contracts={expectedContracts:N0}, " +
        $"transactions={expectedTransactions:N0}.");
}

Console.WriteLine(
    $"Seeding {customerCount:N0} customers, " +
    $"{expectedRelationships:N0} relationships, " +
    $"{expectedContracts:N0} contracts and " +
    $"{expectedTransactions:N0} transactions...");

await using var transaction = await db.Database.BeginTransactionAsync();

var customers = new List<Customer>(customerCount);

for (var i = 1; i <= customerCount; i++)
{
    customers.Add(new Customer
    {
        Id = i,
        CustomerKey = DeterministicGuid("customer", i),
        FirstName = $"Customer{i}",
        LastName = "Benchmark",
        FullName = $"Customer {i} Benchmark",
        CustomerType = CustomerType.Person
    });
}

await db.Customer.AddRangeAsync(customers);
await db.SaveChangesAsync();

var relationshipId = 1;
var contractId = 1;
var transactionId = 1;
var accountId = 1;

var accounts = new List<Account>(expectedContracts);
var relationships =
    new List<CustomerBankingRelationship>(expectedRelationships);
var contracts = new List<Contract>(expectedContracts);
var transactions = new List<Transaction>(expectedTransactions);

for (var customerIndex = 1;
     customerIndex <= customerCount;
     customerIndex++)
{
    var customer = customers[customerIndex - 1];

    for (var r = 0; r < relationshipsPerCustomer; r++)
    {
        relationships.Add(new CustomerBankingRelationship
        {
            Id = relationshipId,
            CustomerBankingRelationshipKey =
                DeterministicGuid("relationship", relationshipId),
            CustomerId = customerIndex,
            CustomerKey = customer.CustomerKey
        });

        for (var c = 0; c < contractsPerRelationship; c++)
        {
            var account = new Account
            {
                Id = accountId,
                AccountKey = DeterministicGuid("account", accountId),
                AccountNumber = $"ACC-{accountId:D10}",
                AccountName = $"Benchmark Account {accountId}"
            };

            accounts.Add(account);

            var contract = new Contract
            {
                Id = contractId,
                ContractKey = DeterministicGuid("contract", contractId),
                ContractType = (ContractType)(contractId % 3),
                Amount = 1000m + contractId,
                AccountId = accountId,
                CustomerBankingRelationshipId = relationshipId
            };

            contracts.Add(contract);

            for (var t = 0; t < transactionsPerContract; t++)
            {
                transactions.Add(new Transaction
                {
                    Id = transactionId,
                    TransactionKey =
                        DeterministicGuid("transaction", transactionId),
                    Amount = 10m + t,
                    Balance = 1000m + contractId - t,
                    ContractId = contractId,
                    ContractKey = contract.ContractKey,
                    AccountId = accountId,
                    AccountKey = account.AccountKey
                });

                transactionId++;
            }

            contractId++;
            accountId++;
        }

        relationshipId++;
    }
}

await db.Account.AddRangeAsync(accounts);
await db.CustomerBankingRelationship.AddRangeAsync(relationships);
await db.Contract.AddRangeAsync(contracts);
await db.Transaction.AddRangeAsync(transactions);

await db.SaveChangesAsync();
await transaction.CommitAsync();

await SetSequenceAsync(db, "Banking", "Customer");
await SetSequenceAsync(db, "Banking", "CustomerBankingRelationship");
await SetSequenceAsync(db, "Lending", "Contract");
await SetSequenceAsync(db, "Lending", "Transaction");
await SetSequenceAsync(db, "Accounting", "Account");

await ValidateBenchmarkFixtureAsync(
    db,
    relationshipsPerCustomer,
    contractsPerRelationship,
    transactionsPerContract);

Console.WriteLine(
    $"Seeded: {customers.Count:N0} customers, " +
    $"{relationships.Count:N0} relationships, " +
    $"{contracts.Count:N0} contracts, " +
    $"{transactions.Count:N0} transactions.");

Console.WriteLine(
    $"Benchmark graph for Customer 1: " +
    $"{relationshipsPerCustomer} relationships -> " +
    $"{relationshipsPerCustomer * contractsPerRelationship} contracts -> " +
    $"{relationshipsPerCustomer * contractsPerRelationship * transactionsPerContract} transactions.");

Console.WriteLine("Customer 1 is the deterministic benchmark target.");

static async Task<(int Customers, int Relationships, int Contracts, int Transactions)>
    GetCountsAsync(BankingEntityContext db)
{
    return (
        await db.Customer.CountAsync(),
        await db.CustomerBankingRelationship.CountAsync(),
        await db.Contract.CountAsync(),
        await db.Transaction.CountAsync());
}

static async Task ValidateBenchmarkFixtureAsync(
    BankingEntityContext db,
    int relationshipsPerCustomer,
    int contractsPerRelationship,
    int transactionsPerContract)
{
    var customer = await db.Customer
        .SingleOrDefaultAsync(x => x.Id == 1);

    if (customer is null)
        throw new InvalidOperationException(
            "Benchmark fixture is missing Customer 1.");

    var relationshipCount =
        await db.CustomerBankingRelationship
            .CountAsync(x => x.CustomerId == customer.Id);

    if (relationshipCount != relationshipsPerCustomer)
    {
        throw new InvalidOperationException(
            $"Customer 1 has {relationshipCount} relationships; " +
            $"expected {relationshipsPerCustomer}.");
    }

    var relationshipIds =
        await db.CustomerBankingRelationship
            .Where(x => x.CustomerId == customer.Id)
            .Select(x => x.Id)
            .ToListAsync();

    var contractCount =
        await db.Contract
            .CountAsync(x =>
                relationshipIds.Contains(
                    (int)x.CustomerBankingRelationshipId!));

    var expectedContracts =
        relationshipsPerCustomer * contractsPerRelationship;

    if (contractCount != expectedContracts)
    {
        throw new InvalidOperationException(
            $"Customer 1 graph has {contractCount} contracts; " +
            $"expected {expectedContracts}.");
    }

    var contractIds =
        await db.Contract
            .Where(x =>
                relationshipIds.Contains(
                    (int)x.CustomerBankingRelationshipId!))
            .Select(x => x.Id)
            .ToListAsync();

    var transactionCount =
        await db.Transaction
            .CountAsync(x =>
                contractIds.Contains((int)x.ContractId!));

    var expectedTransactions =
        expectedContracts * transactionsPerContract;

    if (transactionCount != expectedTransactions)
    {
        throw new InvalidOperationException(
            $"Customer 1 graph has {transactionCount} transactions; " +
            $"expected {expectedTransactions}.");
    }
}

static async Task SetSequenceAsync(
    BankingEntityContext db,
    string schema,
    string table)
{
    var sql = $"""
               SELECT setval(
                   pg_get_serial_sequence('"{schema}"."{table}"', 'Id'),
                   COALESCE(
                       (SELECT MAX("Id") FROM "{schema}"."{table}"),
                       1
                   ),
                   true
               );
               """;

    await db.Database.ExecuteSqlRawAsync(sql);
}

static int GetInt(string name, int fallback) =>
    int.TryParse(
        Environment.GetEnvironmentVariable(name),
        out var value) && value > 0
        ? value
        : fallback;

static Guid DeterministicGuid(string prefix, int value) =>
    GuidUtility.Create(
        GuidUtility.UrlNamespace,
        $"coffee-beanery/{prefix}/{value}");

static class GuidUtility
{
    public static readonly Guid UrlNamespace =
        new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();

        SwapByteOrder(namespaceBytes);

        var nameBytes =
            System.Text.Encoding.UTF8.GetBytes(name);

        var data =
            new byte[namespaceBytes.Length + nameBytes.Length];

        Buffer.BlockCopy(
            namespaceBytes,
            0,
            data,
            0,
            namespaceBytes.Length);

        Buffer.BlockCopy(
            nameBytes,
            0,
            data,
            namespaceBytes.Length,
            nameBytes.Length);

        using var sha1 =
            System.Security.Cryptography.SHA1.Create();

        var hash = sha1.ComputeHash(data);

        var result = new byte[16];

        Array.Copy(hash, result, 16);

        result[6] =
            (byte)((result[6] & 0x0f) | 0x50);

        result[8] =
            (byte)((result[8] & 0x3f) | 0x80);

        SwapByteOrder(result);

        return new Guid(result);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}