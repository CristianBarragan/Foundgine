using Npgsql;
using System.Security.Cryptography;
using System.Text;

var cs = Environment.GetEnvironmentVariable("BankingConnectionString")
         ?? throw new InvalidOperationException("BankingConnectionString is not configured.");

var customers = int.TryParse(
    Environment.GetEnvironmentVariable("TRANSFER_FUNDS_CUSTOMERS"),
    out var c)
    ? c
    : 10;

if (customers < 1)
    throw new ArgumentOutOfRangeException(nameof(customers));

var schemaPath = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "..",
        "..",
        "benchmarks",
        "AgentEndToEnd",
        "Fixtures",
        "HighAssurance.Postgres",
        "schema.sql"));

var schema = await File.ReadAllTextAsync(schemaPath);

await using var dataSource = NpgsqlDataSource.Create(cs);
await using var connection = await dataSource.OpenConnectionAsync();

await using (var schemaCommand = new NpgsqlCommand(schema, connection))
{
    await schemaCommand.ExecuteNonQueryAsync();
}

await using (var clear = new NpgsqlCommand(
                 "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.bank_account;",
                 connection))
{
    await clear.ExecuteNonQueryAsync();
}

var actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

await using var tx = await connection.BeginTransactionAsync();

for (var i = 1; i <= customers; i++)
{
    var source = DeterministicGuid($"run5:{i}:source");
    var destination = DeterministicGuid($"run5:{i}:destination");

    await InsertAccount(connection, tx, source, actor);
    await InsertAccount(connection, tx, destination, actor);
}

await tx.CommitAsync();

Console.WriteLine(
    $"Seeded {customers} transfer pairs / {customers * 2} accounts.");

static async Task InsertAccount(
    NpgsqlConnection connection,
    NpgsqlTransaction tx,
    Guid id,
    Guid owner)
{
    await using var cmd = new NpgsqlCommand(
        """
        INSERT INTO banking.bank_account(
            id,
            tenant_id,
            owner_id,
            balance,
            pending_transactions,
            regulatory_hold,
            daily_transferred,
            daily_limit,
            is_frozen)
        VALUES (
            @id,
            1,
            @owner,
            1000000,
            0,
            0,
            0,
            100000000,
            false);
        """,
        connection,
        tx);

    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("owner", owner);

    await cmd.ExecuteNonQueryAsync();
}

static Guid DeterministicGuid(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return new Guid(bytes[..16]);
}