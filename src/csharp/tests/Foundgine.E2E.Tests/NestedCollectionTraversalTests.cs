using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using FoundgineExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using BankingModel = Foundgine.E2E.Tests.Banking.BankingSemanticModel;
using Foundgine.E2E.Tests.Banking;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Canonical proof that collection traversal composes across more than one hop.
/// The semantic graph knows the connections; the SQL provider only lowers the
/// resulting traversal into correlated EXISTS predicates.
/// </summary>
public sealed class NestedCollectionTraversalTests
{
    [Fact]
    public async Task Nested_some_traversal_filters_parent_through_two_collections()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        var request = new SemanticRequest(
            BankingModel.Customer,
            [new SemanticSelection(new FieldId(2), null, [])],
            new SemanticQueryOptions(
                new SemanticRelationshipFilter(
                    BankingModel.CustomerAccounts,
                    SemanticRelationshipQuantifier.Some,
                    new SemanticRelationshipFilter(
                        BankingModel.AccountTransactions,
                        SemanticRelationshipQuantifier.Some,
                        new SemanticFieldFilter(
                            new FieldId(3),
                            SemanticFilterOperator.Eq,
                            25.00m)))));

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sql = new SqlCompiler(metadata).Compile(plan);

        Assert.Contains("EXISTS", sql.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AccountId", sql.CommandText, StringComparison.Ordinal);
        Assert.Contains("CustomerId", sql.CommandText, StringComparison.Ordinal);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(sql, new FoundgineExecutionContext());

        var row = Assert.Single(result.Rows);
        Assert.Equal("Alice", row.Values["__fg_0_Name"]);
    }

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL);
                              CREATE TABLE "Account" ("Id" INTEGER PRIMARY KEY, "CustomerId" INTEGER NOT NULL, "Balance" DECIMAL NOT NULL);
                              CREATE TABLE "Transaction" ("Id" INTEGER PRIMARY KEY, "AccountId" INTEGER NOT NULL, "Amount" DECIMAL NOT NULL, "TransactionDate" TEXT NOT NULL);
                              INSERT INTO "Customer" VALUES (1, 'Alice');
                              INSERT INTO "Customer" VALUES (2, 'Bob');
                              INSERT INTO "Account" VALUES (10, 1, 100.50);
                              INSERT INTO "Account" VALUES (20, 2, 50.00);
                              INSERT INTO "Transaction" VALUES (100, 10, 25.00, '2026-01-01');
                              INSERT INTO "Transaction" VALUES (101, 20, 99.00, '2026-01-02');
                              """;
        await command.ExecuteNonQueryAsync();
    }
}