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
/// : the canonical Foundgine core proof. A single request exercises
/// AOT-known domain topology, collection traversal, authorization, planning,
/// provider execution, and evidence.
/// </summary>
public sealed class FoundgineCoreProofTests
{
    [Fact]
    public async Task Core_vertical_slice_executes_authorized_nested_collection_and_returns_evidence()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        var tenantPolicy = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ContextParameter("user"),
                "TenantId"),
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ResourceParameter("resource"),
                "TenantId"));

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
                            15000m)))));

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);

        // Policy is attached after resolution, preserving the resolved
        // collection traversal and query options.
        var sourceRoot = resolved.Nodes.Single(x => x.ParentId is null);
        var authorized = resolved.WithAuthorization(sourceRoot.Id, tenantPolicy);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sql = new SqlCompiler(metadata).Compile(plan);

        Assert.Contains("EXISTS", sql.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TenantId", sql.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@auth", sql.CommandText, StringComparison.OrdinalIgnoreCase);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection).ExecuteAsync(
            sql,
            new FoundgineExecutionContext(new Dictionary<string, object?>
            {
                ["user.TenantId"] = 7
            }));

        var row = Assert.Single(result.Rows);
        Assert.Equal("Alice", row.Values["__fg_0_Name"]);

        Assert.NotNull(result.Evidence);
        var evidence = result.Evidence!;
        Assert.Equal("sql", evidence.Provider);
        Assert.Contains(sourceRoot.Id, evidence.AuthorizedNodeIds);
        Assert.Equal(1, evidence.RowsReturned);
        Assert.False(string.IsNullOrWhiteSpace(evidence.PlanFingerprint));
        Assert.False(string.IsNullOrWhiteSpace(evidence.ProviderOperationFingerprint));
    }

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL, "TenantId" INTEGER NOT NULL);
                              CREATE TABLE "Account" ("Id" INTEGER PRIMARY KEY, "CustomerId" INTEGER NOT NULL, "Balance" DECIMAL NOT NULL);
                              CREATE TABLE "Transaction" ("Id" INTEGER PRIMARY KEY, "AccountId" INTEGER NOT NULL, "Amount" DECIMAL NOT NULL, "TransactionDate" TEXT NOT NULL);

                              INSERT INTO "Customer" VALUES (1, 'Alice', 7);
                              INSERT INTO "Customer" VALUES (2, 'Bob', 9);

                              INSERT INTO "Account" VALUES (10, 1, 100.50);
                              INSERT INTO "Account" VALUES (20, 2, 500.00);

                              INSERT INTO "Transaction" VALUES (100, 10, 15000.00, '2026-01-01');
                              INSERT INTO "Transaction" VALUES (101, 20, 25000.00, '2026-01-02');
                              """;
        await command.ExecuteNonQueryAsync();
    }
}