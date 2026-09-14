using Foundgine.E2E.Tests.Banking;
using Foundgine.Core.Execution;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using BankingModel = Foundgine.E2E.Tests.Banking.BankingSemanticModel;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class FoundgineGraphQlPipelineTests
{
    [Fact]
    public async Task GraphQL_query_is_translated_then_runs_through_foundgine_pipeline()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        const string graphql = """
                               query {
                                 customer {
                                   id
                                   name
                                   accounts {
                                     id
                                     transactions {
                                       id
                                       amount
                                     }
                                   }
                                 }
                               }
                               """;

        // Hot Chocolate types stop at the adapter boundary.
        var request = new HotChocolateSemanticAdapter(model).Adapt(graphql);

        // The core pipeline remains protocol-neutral.
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var executionPlan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sqlPlan = new SqlCompiler(metadata).Compile(executionPlan);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(sqlPlan, new ExecutionContext());

        Assert.Equal(2, result.Rows.Count);
        Assert.Contains("__fg_0_Id", result.Rows[0].Values.Keys);
        Assert.Contains("__fg_0_Name", result.Rows[0].Values.Keys);
        Assert.Contains("__fg_2_Amount", result.Rows[0].Values.Keys);
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
                              INSERT INTO "Transaction" VALUES (100, 10, 25.00, '2026-01-01');
                              INSERT INTO "Transaction" VALUES (101, 10, 75.50, '2026-01-02');
                              """;
        await command.ExecuteNonQueryAsync();
    }
}