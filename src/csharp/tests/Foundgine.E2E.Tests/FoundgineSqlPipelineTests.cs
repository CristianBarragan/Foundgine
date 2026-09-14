using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Results;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using BankingModel = Foundgine.E2E.Tests.Banking.BankingSemanticModel;
using Foundgine.E2E.Tests.Banking;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class FoundgineSqlPipelineTests
{
    [Fact]
    public async Task Banking_pipeline_executes_against_real_sqlite()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        var request = new SemanticRequest(
            BankingModel.Customer,
            [
                new SemanticSelection(new FieldId(1), null, []),
                new SemanticSelection(new FieldId(2), null, []),
                new SemanticSelection(
                    null,
                    BankingModel.CustomerAccounts,
                    [
                        new SemanticSelection(new FieldId(1), null, []),
                        new SemanticSelection(
                            null,
                            BankingModel.AccountTransactions,
                            [
                                new SemanticSelection(new FieldId(1), null, []),
                                new SemanticSelection(new FieldId(3), null, [])
                            ])
                    ])
            ]);

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var executionPlan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sqlPlan = new SqlCompiler(metadata).Compile(executionPlan);

        Assert.Contains("SELECT", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INNER JOIN", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Customer", sqlPlan.CommandText, StringComparison.Ordinal);
        Assert.Contains("Account", sqlPlan.CommandText, StringComparison.Ordinal);
        Assert.Contains("Transaction", sqlPlan.CommandText, StringComparison.Ordinal);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var provider = new SqlExecutionProvider(connection);
        var result = await provider.ExecuteAsync(sqlPlan, new ExecutionContext());

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(5, result.Rows[0].Values.Count);
        Assert.Contains("__fg_0_Id", result.Rows[0].Values.Keys);
        Assert.Contains("__fg_2_Amount", result.Rows[0].Values.Keys);
    }


    [Fact]
    public async Task Banking_pipeline_applies_root_filter_order_and_pagination()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();
        var request = new SemanticRequest(
            BankingModel.Customer,
            [
                new SemanticSelection(new FieldId(1), null, []),
                new SemanticSelection(new FieldId(2), null, [])
            ],
            new Foundgine.Core.Semantic.Query.SemanticQueryOptions(
                new Foundgine.Core.Semantic.Query.SemanticFieldFilter(
                    new FieldId(2),
                    Foundgine.Core.Semantic.Query.SemanticFilterOperator.Eq,
                    "Alice"),
                [
                    new Foundgine.Core.Semantic.Query.SemanticOrderTerm(
                        new FieldId(2),
                        Foundgine.Core.Semantic.Query.SemanticSortDirection.Desc)
                ],
                Limit: 1));

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var executionPlan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sqlPlan = new SqlCompiler(metadata).Compile(executionPlan);

        Assert.Contains(" WHERE ", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" ORDER BY ", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" LIMIT @__fg_limit", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Single(sqlPlan.EffectiveParameters, x => x.ContextPath is null);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(sqlPlan, PaginationExecutionContext.Create(1));

        var row = Assert.Single(result.Rows);
        Assert.Equal("Alice", row.Values["__fg_0_Name"]);
    }

    [Fact]
    public async Task Banking_pipeline_materializes_flat_rows_into_semantic_tree()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        var request = new SemanticRequest(
            BankingModel.Customer,
            [
                new SemanticSelection(new FieldId(1), null, []),
                new SemanticSelection(new FieldId(2), null, []),
                new SemanticSelection(null, BankingModel.CustomerAccounts, [
                    new SemanticSelection(new FieldId(1), null, []),
                    new SemanticSelection(null, BankingModel.AccountTransactions, [
                        new SemanticSelection(new FieldId(1), null, []),
                        new SemanticSelection(new FieldId(3), null, [])
                    ])
                ])
            ]);

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(
            new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sqlPlan = new SqlCompiler(metadata).Compile(plan);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(sqlPlan, new ExecutionContext());

        var materialized = new ResultMaterializer(model).Materialize(plan, result);

        var customer = Assert.Single(materialized.Roots);
        Assert.Equal(1, Convert.ToInt32(customer.Values[new FieldId(1)]));

        var account = Assert.Single(customer.Children[BankingModel.CustomerAccounts]);
        Assert.Equal(10, Convert.ToInt32(account.Values[new FieldId(1)]));

        var transactions = account.Children[BankingModel.AccountTransactions];
        Assert.Equal(2, transactions.Count);
        Assert.Equal(100, Convert.ToInt32(transactions[0].Values[new FieldId(1)]));
        Assert.Equal(101, Convert.ToInt32(transactions[1].Values[new FieldId(1)]));
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