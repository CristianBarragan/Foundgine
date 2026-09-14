using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres;
using Foundgine.HighAssurance.Postgres.Execution;
using Foundgine.Core.Semantic.Authorization;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>
/// Deliberately hostile tests for the consequential TransferFunds boundary.
/// These are penetration-style tests: the authorization callback is treated as
/// potentially compromised, request state is mutated between attempts, and
/// batch inputs are constructed to bypass per-item checks.
/// </summary>
public sealed class TransferFundsPenetrationTests
{
    [PostgresFact]
    public async Task Compromised_authorizer_cannot_transfer_from_an_account_the_actor_does_not_own()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 501;
        var actor = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAccountsAsync(dataSource, tenant, source, victim, destination, actor, dailyLimit: 1_000_000m);

        // The authorization dependency is intentionally malicious: it claims ALLOW
        // even though the actor does not own the source account.
        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(
            dataSource,
            static (_, _, _) => new AuthorizationDecision(true, 1, "compromised-authorizer-allow")));

        await Assert.ThrowsAsync<SemanticAuthorizationException>(() => service.ExecuteAsync(
            actor, tenant, new TransferFundsCommand(source, destination, 100m, "pentest-owner-bypass")));

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.Source);
        Assert.Equal(1000m, state.Destination);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Daily_limit_cannot_be_bypassed_by_a_single_transfer()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 502;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAccountsAsync(dataSource, tenant, source, actor, destination, actor, dailyLimit: 50m);

        var service = AuthorizedService(dataSource);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            actor, tenant, new TransferFundsCommand(source, destination, 51m, "pentest-daily-limit")));

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.Source);
        Assert.Equal(1000m, state.Destination);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Batch_cannot_bypass_daily_limit_by_splitting_one_source_across_commands()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 503;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destinationA = Guid.NewGuid();
        var destinationB = Guid.NewGuid();
        await SeedAccountsAsync(dataSource, tenant, source, actor, destinationA, actor, dailyLimit: 75m);
        await InsertAccountAsync(dataSource, tenant, destinationB, actor, 1000m, 1_000_000m);

        var service = AuthorizedService(dataSource);
        var commands = new[]
        {
            new TransferFundsCommand(source, destinationA, 50m, "pentest-batch-limit-a"),
            new TransferFundsCommand(source, destinationB, 50m, "pentest-batch-limit-b")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteBatchAsync(actor, tenant, commands));

        var state = await ReadStateAsync(dataSource, source, destinationA);
        Assert.Equal(1000m, state.Source);
        Assert.Equal(1000m, state.Destination);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Batch_cannot_bypass_available_funds_by_splitting_one_source_across_commands()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 5031;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destinationA = Guid.NewGuid();
        var destinationB = Guid.NewGuid();
        await InsertAccountAsync(dataSource, tenant, source, actor, 1000m, 1_000_000m);
        await SetHoldsAsync(dataSource, source, 600m);
        await InsertAccountAsync(dataSource, tenant, destinationA, actor, 1000m, 1_000_000m);
        await InsertAccountAsync(dataSource, tenant, destinationB, actor, 1000m, 1_000_000m);

        var service = AuthorizedService(dataSource);
        var commands = new[]
        {
            new TransferFundsCommand(source, destinationA, 250m, "pentest-available-a"),
            new TransferFundsCommand(source, destinationB, 250m, "pentest-available-b")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteBatchAsync(actor, tenant, commands));
    }

    [PostgresFact]
    public async Task Tenant_context_cannot_be_changed_to_escape_the_account_tenant()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var realTenant = 504;
        var attackerTenant = 9999;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAccountsAsync(dataSource, realTenant, source, actor, destination, actor, dailyLimit: 1_000_000m);

        var service = AuthorizedService(dataSource);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            actor, attackerTenant, new TransferFundsCommand(source, destination, 10m, "pentest-tenant-escape")));

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.Source);
        Assert.Equal(1000m, state.Destination);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Compromised_authorizer_cannot_bypass_daily_limit()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 505;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAccountsAsync(dataSource, tenant, source, actor, destination, actor, dailyLimit: 10m);

        var service = new PostgresTransferFundsService(new PostgresTransferFundsExecutor(
            dataSource,
            static (_, _, _) => new AuthorizationDecision(true, 1, "compromised-authorizer-allow")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            actor, tenant, new TransferFundsCommand(source, destination, 11m, "pentest-limit-authorizer")));
    }

    private static PostgresTransferFundsService AuthorizedService(NpgsqlDataSource dataSource) =>
        new(new PostgresTransferFundsExecutor(
            dataSource,
            static (id, source, destination) => new AuthorizationDecision(
                id == source.OwnerId && id == destination.OwnerId,
                1,
                $"owner:{id}")));

    private static async Task PrepareAsync(NpgsqlDataSource dataSource)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
        await using var clear = dataSource.CreateCommand(
            "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.bank_account;");
        await clear.ExecuteNonQueryAsync();
    }

    private static async Task SeedAccountsAsync(
        NpgsqlDataSource dataSource,
        int tenant,
        Guid source,
        Guid sourceOwner,
        Guid destination,
        Guid destinationOwner,
        decimal dailyLimit)
    {
        await InsertAccountAsync(dataSource, tenant, source, sourceOwner, 1000m, dailyLimit);
        await InsertAccountAsync(dataSource, tenant, destination, destinationOwner, 1000m, dailyLimit);
    }

    private static async Task InsertAccountAsync(
        NpgsqlDataSource dataSource,
        int tenant,
        Guid id,
        Guid owner,
        decimal balance,
        decimal dailyLimit)
    {
        const string sql = """
                           INSERT INTO banking.bank_account
                           (id, tenant_id, owner_id, balance, pending_transactions, regulatory_hold, daily_transferred, daily_limit, is_frozen)
                           VALUES (@id, @tenant, @owner, @balance, 0, 0, 0, @limit, false);
                           """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("balance", balance);
        command.Parameters.AddWithValue("limit", dailyLimit);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetHoldsAsync(NpgsqlDataSource dataSource, Guid accountId, decimal pending)
    {
        await using var command = dataSource.CreateCommand(
            "UPDATE banking.bank_account SET pending_transactions = @pending WHERE id = @id;");
        command.Parameters.AddWithValue("id", accountId);
        command.Parameters.AddWithValue("pending", pending);
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

    private sealed record State(decimal Source, decimal Destination, long IdempotencyCount, long AuditCount);
}