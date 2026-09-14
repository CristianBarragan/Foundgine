using Foundgine.HighAssurance.Postgres;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

public sealed class PostgresTransactionIsolationSecurityTests
{
    [Fact]
    public void TransferFunds_contract_requires_explicit_read_committed_isolation()
    {
        var contract = PostgresMutationSecurityConformance.TransferFunds;

        Assert.Contains("mutation.transaction.read-committed-isolation", contract.RequiredInvariants);
        Assert.True(contract.UsesExplicitReadCommittedIsolation);
        Assert.Empty(contract.MissingRequirements());
    }

    [PostgresFact]
    public async Task TransferFunds_transaction_uses_explicit_read_committed_isolation()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        await using var command = new NpgsqlCommand("SHOW transaction_isolation;", connection, transaction);

        var value = (string?)await command.ExecuteScalarAsync();

        Assert.Equal("read committed", value);
        await transaction.RollbackAsync();
    }
}
