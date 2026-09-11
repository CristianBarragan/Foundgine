using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using Foundgine.E2E.Tests.Banking;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class JsonIntentPipelineTests
{
    [Fact]
    public async Task Json_intent_drives_the_same_semantic_pipeline_as_other_producers()
    {
        const string json = """
                            {
                              "rootEntity": "Transaction",
                              "selections": [
                                { "field": "Id" },
                                { "field": "Amount" },
                                { "field": "TransactionDate" }
                              ],
                              "filter": {
                                "kind": "relationship",
                                "relationship": "Account",
                                "quantifier": "Some",
                                "predicate": {
                                  "kind": "relationship",
                                  "relationship": "Customer",
                                  "quantifier": "Some",
                                  "predicate": {
                                    "kind": "field",
                                    "field": "Name",
                                    "operator": "Eq",
                                    "value": "Alice"
                                  }
                                }
                              },
                              "order": [
                                { "field": "TransactionDate", "direction": "Desc" }
                              ],
                              "limit": 5
                            }
                            """;

        var model = BankingSemanticModel.Build();
        var metadata = BankingRelationalMetadata.Build();
        var intent = new JsonReadIntentAdapter().Parse(json);
        var request = new Foundgine.Core.Semantic.Intent.ReadIntentCompiler(model).Compile(intent);

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sqlPlan = new SqlCompiler(metadata).Compile(plan);

        Assert.Contains("WHERE", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT @__fg_limit", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alice", sqlPlan.EffectiveParameters.Select(x => x.Value?.ToString()));

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(sqlPlan, PaginationExecutionContext.Create(5));

        Assert.Equal(5, result.Rows.Count);
        Assert.Equal(new object[] { 106L, 105L, 104L, 103L, 102L },
            result.Rows.Select(row => row.Values["__fg_0_Id"]).ToArray());
    }

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" (
                                  "Id" INTEGER PRIMARY KEY,
                                  "Name" TEXT NOT NULL
                              );
                              CREATE TABLE "Account" (
                                  "Id" INTEGER PRIMARY KEY,
                                  "CustomerId" INTEGER NOT NULL,
                                  "Balance" DECIMAL NOT NULL
                              );
                              CREATE TABLE "Transaction" (
                                  "Id" INTEGER PRIMARY KEY,
                                  "AccountId" INTEGER NOT NULL,
                                  "Amount" DECIMAL NOT NULL,
                                  "TransactionDate" TEXT NOT NULL
                              );
                              INSERT INTO "Customer" VALUES (1, 'Alice');
                              INSERT INTO "Customer" VALUES (2, 'Bob');
                              INSERT INTO "Account" VALUES (10, 1, 100.50);
                              INSERT INTO "Account" VALUES (20, 2, 200.00);
                              INSERT INTO "Transaction" VALUES (101, 10, 10.00, '2026-01-01');
                              INSERT INTO "Transaction" VALUES (102, 10, 20.00, '2026-01-02');
                              INSERT INTO "Transaction" VALUES (103, 10, 30.00, '2026-01-03');
                              INSERT INTO "Transaction" VALUES (104, 10, 40.00, '2026-01-04');
                              INSERT INTO "Transaction" VALUES (105, 10, 50.00, '2026-01-05');
                              INSERT INTO "Transaction" VALUES (106, 10, 60.00, '2026-01-06');
                              INSERT INTO "Transaction" VALUES (107, 20, 70.00, '2026-01-07');
                              INSERT INTO "Transaction" VALUES (108, 20, 80.00, '2026-01-08');
                              """;
        await command.ExecuteNonQueryAsync();
    }
}