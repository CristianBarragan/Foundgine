using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.E2E.Tests.Banking;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using BankingModel = Foundgine.E2E.Tests.Banking.BankingSemanticModel;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class CursorPaginationTests
{
    [Fact]
    public async Task Forward_cursor_returns_next_page_and_page_info()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        var first = BuildRequest();
        var firstPlan = new SqlCompiler(metadata).Compile(
            new Planner().Plan(
                    new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(
                        new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(first))) with
                {
                    AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
                });

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);
        var provider = new SqlExecutionProvider(connection);

        var firstResult = await provider.ExecuteAsync(firstPlan, PaginationExecutionContext.Create(2));
        Assert.Equal(2, firstResult.Rows.Count);
        Assert.NotNull(firstResult.PageInfo);
        Assert.NotNull(firstResult.PageInfo!.StartCursor);
        Assert.NotNull(firstResult.PageInfo.EndCursor);
        Assert.True(firstResult.PageInfo.HasNextPage);
        Assert.False(firstResult.PageInfo.HasPreviousPage);

        var second = BuildRequest(firstResult.PageInfo.EndCursor);
        var secondPlan = new SqlCompiler(metadata).Compile(
            new Planner().Plan(
                    new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(
                        new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(second))) with
                {
                    AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
                });

        var secondResult = await provider.ExecuteAsync(secondPlan,
            PaginationExecutionContext.Create(2, firstResult.PageInfo.EndCursor));
        var row = Assert.Single(secondResult.Rows);
        Assert.Equal(3, Convert.ToInt32(row.Values["__fg_0_Id"]));
        Assert.False(secondResult.PageInfo!.HasNextPage);
        Assert.True(secondResult.PageInfo.HasPreviousPage);
    }

    [Fact]
    public void Invalid_cursor_is_rejected_before_sql_generation()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();
        var request = BuildRequest("not-a-valid-cursor");

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new SqlCompiler(metadata).Compile(plan));
        Assert.Contains("pagination cursor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SemanticRequest BuildRequest(string? after = null) => new(
        BankingModel.Customer,
        [
            new SemanticSelection(new FieldId(1), null, []),
            new SemanticSelection(new FieldId(2), null, [])
        ],
        new SemanticQueryOptions(Limit: 2, After: after));

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL);
                              INSERT INTO "Customer" VALUES (1, 'Alice');
                              INSERT INTO "Customer" VALUES (2, 'Bob');
                              INSERT INTO "Customer" VALUES (3, 'Carol');
                              """;
        await command.ExecuteNonQueryAsync();
    }
}