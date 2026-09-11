using Foundgine.Runtime;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Providers.Storage.Sql;
using Foundgine.E2E.Tests.Banking;
using Npgsql;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// provider gate: the same hostile-agent boundary exercised by the
/// black-box SQLite test is executed against PostgreSQL. The test is skipped
/// unless FOUNDGINE_POSTGRES_CONNECTION_STRING is configured.
/// </summary>
public sealed class PostgresBlackBoxTests
{
    [PostgreSqlFact]
    public async Task Authorized_agent_intent_reaches_postgres_without_cross_tenant_rows()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var searchPath = new NpgsqlCommand("SET LOCAL search_path TO fg_replay;", connection, transaction))
            await searchPath.ExecuteNonQueryAsync();

        await SeedAsync(connection, transaction);

        var engine = new FoundgineEngine(
            new FoundgineOptions
            {
                Model = BankingSemanticModel.Build(),
                AuthorizationPolicy = new TenantPolicy()
            },
            new SqlCompiler(BankingRelationalMetadata.Build()),
            new SqlExecutionProvider(connection, transaction));

        var intent = new JsonReadIntentAdapter().Parse("""
                                                       {
                                                       "rootEntity": "Customer",
                                                       "selections": [
                                                       { "field": "Id" },
                                                       { "field": "Name" }
                                                       ]
                                                       }
                                                       """);

        var result = await engine.ExecuteAsync(
            intent,
            new ExecutionContext(new Dictionary<string, object?>
            {
                ["user.TenantId"] = 7
            }));

        var row = Assert.Single(result.Rows);
        Assert.True(Convert.ToInt64(row.Values["__fg_0_Id"]) > 0);
        Assert.Equal("Alice", row.Values["__fg_0_Name"]);
        Assert.NotNull(result.Evidence);
        Assert.NotNull(result.Receipt);
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task SQL_injection_shaped_agent_value_is_parameterized_by_postgres_execution()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var searchPath = new NpgsqlCommand("SET LOCAL search_path TO fg_replay;", connection, transaction))
            await searchPath.ExecuteNonQueryAsync();

        await SeedAsync(connection, transaction);

        var engine = new FoundgineEngine(
            new FoundgineOptions
            {
                Model = BankingSemanticModel.Build(),
                AuthorizationPolicy = new TenantPolicy()
            },
            new SqlCompiler(BankingRelationalMetadata.Build()),
            new SqlExecutionProvider(connection, transaction));

        var intent = new JsonReadIntentAdapter().Parse("""
                                                       {
                                                       "rootEntity": "Customer",
                                                       "selections": [
                                                       { "field": "Id" },
                                                       { "field": "Name" }
                                                       ],
                                                       "filter": {
                                                       "kind": "field",
                                                       "field": "Name",
                                                       "operator": "Eq",
                                                       "value": "' OR 1=1 --"
                                                       }
                                                       }
                                                       """);

        var result = await engine.ExecuteAsync(
            intent,
            new ExecutionContext(new Dictionary<string, object?>
            {
                ["user.TenantId"] = 7
            }));

        Assert.Empty(result.Rows);
        await transaction.RollbackAsync();
    }

    private static async Task SeedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        const string sql = """
                           INSERT INTO "Customer" ("Name", "TenantId")
                           VALUES ('Alice', 7), ('Bob', 8);
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TenantPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(
            Foundgine.Core.Abstractions.EntityId entityId,
            AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read && entityId == BankingSemanticModel.Customer
                ? AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.ContextParameter("user"), "TenantId"))
                : null;
    }
}