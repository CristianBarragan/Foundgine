using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres;
using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

internal static class PostgresTestEnvironment
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION")
        ?? Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")
        ?? "";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString);
}

public sealed class PostgresTransferFundsTests
{
    private readonly string _connectionString =
        PostgresTestEnvironment.ConnectionString;

    [PostgresFact]
    public async Task Transfer_is_atomic_and_writes_idempotency_and_audit()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await PrepareAsync(dataSource);

        var tenant = 42;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);

        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId));
        var command = new TransferFundsCommand(source, destination, 10_000m, Guid.NewGuid().ToString("N"));

        var receipt = await service.ExecuteAsync(actor, tenant, command);
        Assert.False(receipt.Replay);
        Assert.Equal(10_000m, receipt.Amount);
        Assert.NotNull(receipt.SecurityProof);
        Assert.True(receipt.SecurityProof!.IsSatisfied);
        Assert.Contains("mutation.atomic", receipt.SecurityProof.Preserved);

        await using var connection = await dataSource.OpenConnectionAsync();
        var state = await ReadStateAsync(connection, source, destination);
        Assert.Equal(90_000m, state.SourceBalance);
        Assert.Equal(60_000m, state.DestinationBalance);
        Assert.Equal(1, state.IdempotencyCount);
        Assert.Equal(1, state.AuditCount);
    }

    [PostgresFact]
    public async Task Replay_returns_original_receipt_without_second_debit()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await PrepareAsync(dataSource);

        var tenant = 43;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);
        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId));
        var command = new TransferFundsCommand(source, destination, 5m, "replay-test-key");

        var first = await service.ExecuteAsync(actor, tenant, command);
        var replay = await service.ExecuteAsync(actor, tenant, command);

        Assert.False(first.Replay);
        Assert.True(replay.Replay);
        Assert.Equal(first.TransferId, replay.TransferId);

        await using var connection = await dataSource.OpenConnectionAsync();
        var state = await ReadStateAsync(connection, source, destination);
        Assert.Equal(99_995m, state.SourceBalance);
        Assert.Equal(50_005m, state.DestinationBalance);
        Assert.Equal(1, state.IdempotencyCount);
        Assert.Equal(1, state.AuditCount);
    }

    [PostgresFact]
    public async Task Failure_rolls_back_debit_credit_idempotency_and_audit()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await PrepareAsync(dataSource);

        var tenant = 44;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination, dailyLimit: 1m);
        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            actor, tenant, new TransferFundsCommand(source, destination, 10m, Guid.NewGuid().ToString("N"))));

        await using var connection = await dataSource.OpenConnectionAsync();
        var state = await ReadStateAsync(connection, source, destination);
        Assert.Equal(100_000m, state.SourceBalance);
        Assert.Equal(50_000m, state.DestinationBalance);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    private static async Task PrepareAsync(NpgsqlDataSource dataSource)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
        await using var clear =
            dataSource.CreateCommand(
                "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.bank_account;");
        await clear.ExecuteNonQueryAsync();
    }

    private static async Task SeedAsync(NpgsqlDataSource dataSource, int tenant, Guid actor, Guid source,
        Guid destination, decimal dailyLimit = 1_000_000m)
    {
        const string sql = """
                           INSERT INTO banking.bank_account(id, tenant_id, owner_id, balance, pending_transactions, regulatory_hold, daily_transferred, daily_limit, is_frozen)
                           VALUES (@source, @tenant, @actor, 100000, 0, 0, 0, @limit, false),
                           (@destination, @tenant, @actor, 50000, 0, 0, 0, @limit, false);
                           """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("limit", dailyLimit);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<State> ReadStateAsync(NpgsqlConnection connection, Guid source, Guid destination)
    {
        const string sql = """
                           SELECT
                           (SELECT balance FROM banking.bank_account WHERE id = @source),
                           (SELECT balance FROM banking.bank_account WHERE id = @destination),
                           (SELECT count(*) FROM banking.transfer_idempotency),
                           (SELECT count(*) FROM banking.transfer_audit);
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("destination", destination);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new State(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private sealed record State(
        decimal SourceBalance,
        decimal DestinationBalance,
        long IdempotencyCount,
        long AuditCount);
}

// adversarial/concurrency coverage lives in a separate partial file so the
// basic provider tests remain easy to read.