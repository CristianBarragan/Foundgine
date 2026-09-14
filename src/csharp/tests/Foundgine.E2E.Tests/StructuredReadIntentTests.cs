using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using BankingModel = Foundgine.E2E.Tests.Banking.BankingSemanticModel;
using Foundgine.E2E.Tests.Banking;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class StructuredReadIntentTests
{
    [Fact]
    public async Task Structured_intent_finds_Alices_five_most_recent_transactions()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        // This is the representation an API or LLM adapter can produce. It
        // contains semantic names and values only; no SQL or ORM expressions.
        var intent = new ReadIntent(
            RootEntity: "Transaction",
            Selections:
            [
                new ReadSelection(Field: "Id"),
                new ReadSelection(Field: "Amount"),
                new ReadSelection(Field: "TransactionDate")
            ],
            Filter: new ReadRelationshipFilter(
                "Account",
                SemanticRelationshipQuantifier.Some,
                new ReadRelationshipFilter(
                    "Customer",
                    SemanticRelationshipQuantifier.Some,
                    new ReadFieldFilter("Name", SemanticFilterOperator.Eq, "Alice"))),
            Order:
            [
                new ReadOrder("TransactionDate", SemanticSortDirection.Desc)
            ],
            Limit: 5);

        var request = new ReadIntentCompiler(model).Compile(intent);
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sql = new SqlCompiler(metadata).Compile(plan);

        Assert.Contains("EXISTS", sql.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT @__fg_limit", sql.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Single(sql.EffectiveParameters, x => x.ContextPath is null);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(sql, PaginationExecutionContext.Create(5));

        Assert.Equal(5, result.Rows.Count);
        Assert.Equal(106, Convert.ToInt32(result.Rows[0].Values["__fg_0_Id"]));
        Assert.Equal(102, Convert.ToInt32(result.Rows[^1].Values["__fg_0_Id"]));
        Assert.All(result.Rows, row => Assert.NotEqual(200, row.Values["__fg_0_Id"]));
    }

    [Fact]
    public void Invalid_structured_intent_fails_before_planning()
    {
        var model = BankingModel.Build();
        var intent = new ReadIntent(
            "Transaction",
            [new ReadSelection(Field: "DoesNotExist")]);

        var error = Assert.Throws<InvalidOperationException>(() => { new ReadIntentCompiler(model).Compile(intent); });

        Assert.Contains("Unknown field", error.Message, StringComparison.Ordinal);
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
                              INSERT INTO "Transaction" VALUES (101, 10, 30.00, '2026-01-02');
                              INSERT INTO "Transaction" VALUES (102, 10, 35.00, '2026-01-03');
                              INSERT INTO "Transaction" VALUES (103, 10, 40.00, '2026-01-04');
                              INSERT INTO "Transaction" VALUES (104, 10, 45.00, '2026-01-05');
                              INSERT INTO "Transaction" VALUES (105, 10, 50.00, '2026-01-06');
                              INSERT INTO "Transaction" VALUES (106, 10, 55.00, '2026-01-07');
                              INSERT INTO "Transaction" VALUES (200, 20, 99.00, '2026-01-08');
                              """;
        await command.ExecuteNonQueryAsync();
    }
}