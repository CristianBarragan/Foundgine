using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using FoundgineExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using Foundgine.E2E.Tests.Banking;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Proves that authorization attached to a nested semantic node survives
/// authorization, planning and SQL lowering. A child connection cannot escape
/// the authorization boundary merely by being reached through a relationship.
/// </summary>
public sealed class NestedAuthorizationExecutionTests
{
    [Fact]
    public async Task Nested_authorization_survives_authorization_and_is_enforced_by_sql()
    {
        var model = BankingSemanticModel.Build();
        var childAuthorization = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "Id"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("account"), "Id"));

        var graph = new SemanticGraph();
        var customer = graph.AddRoot(BankingSemanticModel.Customer, [new FieldId(1), new FieldId(2)]);
        graph.Add(
            BankingSemanticModel.Account,
            BankingSemanticModel.CustomerAccounts,
            customer,
            [new FieldId(1), new FieldId(3)],
            childAuthorization);

        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(graph);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sql = new SqlCompiler(BankingRelationalMetadata.Build()).Compile(plan);

        Assert.Contains("@auth", sql.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(plan.Root.Children);
        Assert.NotNull(plan.Root.Children[0].Authorization);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection).ExecuteAsync(
            sql,
            new FoundgineExecutionContext(new Dictionary<string, object?>
            {
                ["user.Id"] = 10
            }));

        var row = Assert.Single(result.Rows);
        Assert.Equal("Alice", row.Values["__fg_0_Name"]);
    }

    [Fact]
    public async Task Nested_authorization_fails_closed_when_context_is_missing()
    {
        var childAuthorization = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "Id"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("account"), "Id"));

        var graph = new SemanticGraph();
        var customer = graph.AddRoot(BankingSemanticModel.Customer, [new FieldId(1), new FieldId(2)]);
        graph.Add(
            BankingSemanticModel.Account,
            BankingSemanticModel.CustomerAccounts,
            customer,
            [new FieldId(1)],
            childAuthorization);

        var plan = new Planner().Plan(
                new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(graph)) with
            {
                AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
            };
        var sql = new SqlCompiler(BankingRelationalMetadata.Build()).Compile(plan);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlExecutionProvider(connection).ExecuteAsync(sql, new FoundgineExecutionContext()));

        Assert.Contains("user.Id", exception.Message, StringComparison.Ordinal);
    }

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL);
                              CREATE TABLE "Account" ("Id" INTEGER PRIMARY KEY, "CustomerId" INTEGER NOT NULL, "Balance" DECIMAL NOT NULL);
                              INSERT INTO "Customer" VALUES (1, 'Alice');
                              INSERT INTO "Customer" VALUES (2, 'Bob');
                              INSERT INTO "Account" VALUES (10, 1, 100.50);
                              INSERT INTO "Account" VALUES (20, 2, 50.00);
                              """;
        await command.ExecuteNonQueryAsync();
    }
}