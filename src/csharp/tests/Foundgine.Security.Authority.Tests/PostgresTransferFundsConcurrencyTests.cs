using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres;
using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

public sealed class PostgresTransferFundsConcurrencyTests
{
    private readonly string? _connectionString = PostgresFactAttribute.PostgresConnectionString;

    [PostgresFact]
    public async Task Same_idempotency_key_concurrent_requests_execute_once()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
        await PrepareAsync(dataSource);

        var tenant = 100;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination, sourceBalance: 1000m, destinationBalance: 0m);

        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId));
        var command = new TransferFundsCommand(source, destination, 100m, "same-key-concurrency");

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => service.ExecuteAsync(actor, tenant, command))
            .ToArray();
        var receipts = await Task.WhenAll(tasks);

        Assert.Single(receipts.Select(x => x.TransferId).Distinct());
        Assert.Single(receipts, x => !x.Replay);
        Assert.Equal(7, receipts.Count(x => x.Replay));

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(900m, state.SourceBalance);
        Assert.Equal(100m, state.DestinationBalance);
        Assert.Equal(1, state.IdempotencyCount);
        Assert.Equal(1, state.AuditCount);
    }

    [PostgresFact]
    public async Task Opposing_transfers_are_serialized_without_deadlock()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
        await PrepareAsync(dataSource);

        var tenant = 101;
        var actor = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, a, b, sourceBalance: 1000m, destinationBalance: 1000m);

        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource,
            static (id, x, y) => id == x.OwnerId && id == y.OwnerId));
        var first = service.ExecuteAsync(actor, tenant, new TransferFundsCommand(a, b, 100m, "a-to-b"));
        var second = service.ExecuteAsync(actor, tenant, new TransferFundsCommand(b, a, 200m, "b-to-a"));

        var completed = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(completed, receipt => Assert.False(receipt.Replay));

        var state = await ReadStateAsync(dataSource, a, b);
        Assert.Equal(1100m, state.SourceBalance);
        Assert.Equal(900m, state.DestinationBalance);
        Assert.Equal(2, state.IdempotencyCount);
        Assert.Equal(2, state.AuditCount);
    }

    [PostgresFact]
    public async Task Cross_tenant_source_or_destination_is_rejected_before_mutation()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
        await PrepareAsync(dataSource);

        var tenant = 102;
        var otherTenant = 999;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination, sourceBalance: 1000m, destinationBalance: 1000m,
            destinationTenant: otherTenant);

        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            actor, tenant, new TransferFundsCommand(source, destination, 100m, "tenant-escape")));

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.SourceBalance);
        Assert.Equal(1000m, state.DestinationBalance);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Frozen_destination_is_rejected_without_partial_write()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
        await PrepareAsync(dataSource);

        var tenant = 103;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination, sourceBalance: 1000m, destinationBalance: 1000m,
            destinationFrozen: true);

        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            actor, tenant, new TransferFundsCommand(source, destination, 100m, "frozen-destination")));

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.SourceBalance);
        Assert.Equal(1000m, state.DestinationBalance);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Authorization_is_rechecked_after_rows_are_locked()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
        await PrepareAsync(dataSource);

        var tenant = 104;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination, sourceBalance: 1000m,
            destinationBalance: 1000m);

        var authorize = false;
        var service =
            new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource, (_, _, _) => authorize));

        await Assert.ThrowsAsync<Foundgine.Core.Semantic.Authorization.SemanticAuthorizationException>(() =>
            service.ExecuteAsync(
                actor, tenant, new TransferFundsCommand(source, destination, 100m, "authorization-recheck")));

        authorize = true;
        var receipt = await service.ExecuteAsync(actor, tenant,
            new TransferFundsCommand(source, destination, 100m, "authorization-recheck-2"));
        Assert.False(receipt.Replay);

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(900m, state.SourceBalance);
        Assert.Equal(1100m, state.DestinationBalance);
    }

    [PostgresFact]
    public async Task Insufficient_available_funds_cannot_be_bypassed_by_raw_balance()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
        await PrepareAsync(dataSource);

        var tenant = 105;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination, sourceBalance: 1000m, destinationBalance: 0m,
            pendingTransactions: 400m, regulatoryHold: 300m);

        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            actor, tenant, new TransferFundsCommand(source, destination, 301m, "available-funds")));

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.SourceBalance);
        Assert.Equal(0m, state.DestinationBalance);
        Assert.Equal(0, state.IdempotencyCount);
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

    private static async Task SeedAsync(
        NpgsqlDataSource dataSource,
        int tenant,
        Guid actor,
        Guid source,
        Guid destination,
        decimal sourceBalance,
        decimal destinationBalance,
        int? destinationTenant = null,
        bool destinationFrozen = false,
        decimal pendingTransactions = 0m,
        decimal regulatoryHold = 0m)
    {
        const string sql = """
                           INSERT INTO banking.bank_account(id, tenant_id, owner_id, balance, pending_transactions, regulatory_hold, daily_transferred, daily_limit, is_frozen)
                           VALUES (@source, @tenant, @actor, @sourceBalance, @pending, @hold, 0, 1000000, false),
                                  (@destination, @destinationTenant, @actor, @destinationBalance, 0, 0, 0, 1000000, @frozen);
                           """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("destinationTenant", destinationTenant ?? tenant);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("sourceBalance", sourceBalance);
        command.Parameters.AddWithValue("destinationBalance", destinationBalance);
        command.Parameters.AddWithValue("pending", pendingTransactions);
        command.Parameters.AddWithValue("hold", regulatoryHold);
        command.Parameters.AddWithValue("frozen", destinationFrozen);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<State> ReadStateAsync(NpgsqlDataSource dataSource, Guid source, Guid destination)
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
        return new State(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private sealed record State(
        decimal SourceBalance,
        decimal DestinationBalance,
        long IdempotencyCount,
        long AuditCount);

    [PostgresFact]
    public async Task Same_idempotency_key_fault_rollback_allows_waiting_request_to_execute_once()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
        await PrepareAsync(dataSource);

        var tenant = 106;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination, sourceBalance: 1000m, destinationBalance: 0m);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inject = true;
        var firstExecutor = new PostgresTransferFundsExecutor(
            dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId,
            point =>
            {
                if (point != PostgresTransferFundsFaultPoint.AfterMutationBeforeCommit || !inject) return;
                inject = false;
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("Injected concurrency fault before commit.");
            });
        var waitingExecutor = new PostgresTransferFundsExecutor(
            dataSource,
            static (id, a, b) => id == a.OwnerId && id == b.OwnerId);
        var first = new PostgresTransferFundsService(firstExecutor);
        var waiting = new PostgresTransferFundsService(waitingExecutor);
        var command = new TransferFundsCommand(source, destination, 100m, "fault-concurrency-same-key");

        var failed = first.ExecuteAsync(actor, tenant, command);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var replayCandidate = waiting.ExecuteAsync(actor, tenant, command);
        await Task.Delay(250);
        Assert.False(replayCandidate.IsCompleted,
            "The second request should remain blocked by the first transaction's advisory lock.");

        release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        var receipt = await replayCandidate.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(receipt.Replay);
        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(900m, state.SourceBalance);
        Assert.Equal(100m, state.DestinationBalance);
        Assert.Equal(1, state.IdempotencyCount);
        Assert.Equal(1, state.AuditCount);
    }

    [PostgresFact]
    public async Task Opposing_transfer_waits_through_fault_then_commits_without_partial_state()
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString!);
        await PrepareAsync(dataSource);

        var tenant = 107;
        var actor = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, a, b, sourceBalance: 1000m, destinationBalance: 1000m);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inject = true;
        var firstExecutor = new PostgresTransferFundsExecutor(
            dataSource,
            static (id, x, y) => id == x.OwnerId && id == y.OwnerId,
            point =>
            {
                if (point != PostgresTransferFundsFaultPoint.AfterMutationBeforeCommit || !inject) return;
                inject = false;
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("Injected opposing-transfer fault before commit.");
            });
        var secondExecutor = new PostgresTransferFundsExecutor(
            dataSource,
            static (id, x, y) => id == x.OwnerId && id == y.OwnerId);
        var first = new PostgresTransferFundsService(firstExecutor);
        var second = new PostgresTransferFundsService(secondExecutor);

        var failed = first.ExecuteAsync(actor, tenant, new TransferFundsCommand(a, b, 100m, "fault-a-to-b"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var waiting = second.ExecuteAsync(actor, tenant, new TransferFundsCommand(b, a, 200m, "wait-b-to-a"));

        await Task.Delay(250);
        Assert.False(waiting.IsCompleted, "The opposing transfer should wait for the locked account rows.");

        release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        var receipt = await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(receipt.Replay);
        var state = await ReadStateAsync(dataSource, a, b);
        Assert.Equal(1200m, state.SourceBalance);
        Assert.Equal(800m, state.DestinationBalance);
        Assert.Equal(1, state.IdempotencyCount);
        Assert.Equal(1, state.AuditCount);
    }
}