using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres;
using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

public sealed class PostgresTransferFundsFaultInjectionTests
{
    [PostgresFact]
    public async Task Fault_after_single_mutation_before_commit_rolls_back_every_security_side_effect()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 90;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);

        var executor = new PostgresTransferFundsExecutor(
            dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId,
            point =>
            {
                if (point == PostgresTransferFundsFaultPoint.AfterMutationBeforeCommit)
                    throw new InvalidOperationException("Injected provider failure after mutation before commit.");
            });
        var service = new PostgresTransferFundsService(executor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            actor, tenant, new TransferFundsCommand(source, destination, 25m, "fault-single")));

        await AssertStateAsync(dataSource, source, destination, 100_000m, 50_000m, 0, 0);
    }

    [PostgresFact]
    public async Task Fault_after_batch_mutation_before_commit_rolls_back_the_entire_batch()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 91;
        var actor = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        await SeedThreeAsync(dataSource, tenant, actor, a, b, c);

        var executor = new PostgresTransferFundsExecutor(
            dataSource,
            static (id, x, y) => id == x.OwnerId && id == y.OwnerId,
            point =>
            {
                if (point == PostgresTransferFundsFaultPoint.AfterBatchMutationBeforeCommit)
                    throw new InvalidOperationException(
                        "Injected provider failure after batch mutation before commit.");
            });
        var service = new PostgresTransferFundsService(executor);

        var commands = new[]
        {
            new TransferFundsCommand(a, b, 10m, "fault-batch-a"),
            new TransferFundsCommand(b, c, 20m, "fault-batch-b")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteBatchAsync(actor, tenant, commands));

        await using var connection = await dataSource.OpenConnectionAsync();
        const string sql = """
                           SELECT
                             (SELECT balance FROM banking.bank_account WHERE id = @a),
                             (SELECT balance FROM banking.bank_account WHERE id = @b),
                             (SELECT balance FROM banking.bank_account WHERE id = @c),
                             (SELECT count(*) FROM banking.transfer_idempotency),
                             (SELECT count(*) FROM banking.transfer_audit);
                           """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("a", a);
        command.Parameters.AddWithValue("b", b);
        command.Parameters.AddWithValue("c", c);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(100_000m, reader.GetDecimal(0));
        Assert.Equal(50_000m, reader.GetDecimal(1));
        Assert.Equal(50_000m, reader.GetDecimal(2));
        Assert.Equal(0L, reader.GetInt64(3));
        Assert.Equal(0L, reader.GetInt64(4));
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
        Guid destination)
    {
        const string sql = """
                           INSERT INTO banking.bank_account(id, tenant_id, owner_id, balance, pending_transactions, regulatory_hold, daily_transferred, daily_limit, is_frozen)
                           VALUES (@source, @tenant, @actor, 100000, 0, 0, 0, 1000000, false),
                                  (@destination, @tenant, @actor, 50000, 0, 0, 0, 1000000, false);
                           """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedThreeAsync(NpgsqlDataSource dataSource, int tenant, Guid actor, Guid a, Guid b,
        Guid c)
    {
        const string sql = """
                           INSERT INTO banking.bank_account(id, tenant_id, owner_id, balance, pending_transactions, regulatory_hold, daily_transferred, daily_limit, is_frozen)
                           VALUES (@a, @tenant, @actor, 100000, 0, 0, 0, 1000000, false),
                                  (@b, @tenant, @actor, 50000, 0, 0, 0, 1000000, false),
                                  (@c, @tenant, @actor, 50000, 0, 0, 0, 1000000, false);
                           """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("a", a);
        command.Parameters.AddWithValue("b", b);
        command.Parameters.AddWithValue("c", c);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertStateAsync(NpgsqlDataSource dataSource, Guid source, Guid destination,
        decimal sourceBalance, decimal destinationBalance, long idempotencyCount, long auditCount)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
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
        Assert.Equal(sourceBalance, reader.GetDecimal(0));
        Assert.Equal(destinationBalance, reader.GetDecimal(1));
        Assert.Equal(idempotencyCount, reader.GetInt64(2));
        Assert.Equal(auditCount, reader.GetInt64(3));
    }
}