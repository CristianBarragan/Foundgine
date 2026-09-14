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

public sealed class CompoundCursorPaginationTests
{
    [Fact]
    public async Task Custom_order_uses_primary_key_tie_breaker_and_compound_cursor()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        var first = BuildRequest(limit: 2);
        var firstPlan = Compile(model, metadata, first);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);
        var provider = new SqlExecutionProvider(connection);

        var firstResult = await provider.ExecuteAsync(firstPlan, PaginationExecutionContext.Create(2));

        Assert.Equal(2, firstResult.Rows.Count);
        Assert.Equal(4, Convert.ToInt32(firstResult.Rows[0].Values["__fg_0_Id"]));
        Assert.Equal(2, Convert.ToInt32(firstResult.Rows[1].Values["__fg_0_Id"]));
        Assert.NotNull(firstResult.PageInfo);
        Assert.True(firstResult.PageInfo!.HasNextPage);
        Assert.False(firstResult.PageInfo.HasPreviousPage);

        var second = BuildRequest(
            limit: 2,
            after: firstResult.PageInfo.EndCursor);

        var secondPlan = Compile(model, metadata, second);
        var secondResult = await provider.ExecuteAsync(secondPlan,
            PaginationExecutionContext.Create(2, firstResult.PageInfo.EndCursor));

        var rows = secondResult.Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(3, Convert.ToInt32(rows[0].Values["__fg_0_Id"]));
        Assert.Equal(1, Convert.ToInt32(rows[1].Values["__fg_0_Id"]));
        Assert.False(secondResult.PageInfo!.HasNextPage);
        Assert.True(secondResult.PageInfo.HasPreviousPage);
    }

    [Fact]
    public async Task Duplicate_sort_values_are_disambiguated_by_primary_key()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        var first = BuildRequest(limit: 1);
        var firstPlan = Compile(model, metadata, first);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedDuplicateNamesAsync(connection);
        var provider = new SqlExecutionProvider(connection);

        var firstResult = await provider.ExecuteAsync(firstPlan, PaginationExecutionContext.Create(1));
        var firstRow = Assert.Single(firstResult.Rows);

        Assert.Equal(4, Convert.ToInt32(firstRow.Values["__fg_0_Id"]));
        Assert.True(firstResult.PageInfo!.HasNextPage);

        var second = BuildRequest(limit: 1, after: firstResult.PageInfo.EndCursor);
        var secondResult = await provider.ExecuteAsync(
            Compile(model, metadata, second),
            PaginationExecutionContext.Create(1, firstResult.PageInfo.EndCursor));

        var secondRow = Assert.Single(secondResult.Rows);
        Assert.Equal(2, Convert.ToInt32(secondRow.Values["__fg_0_Id"]));
    }

    [Fact]
    public void Compound_cursor_order_is_visible_in_sql()
    {
        var model = BankingModel.Build();
        var metadata = BankingRelationalMetadata.Build();

        var request = BuildRequest(limit: 2);
        var plan = Compile(model, metadata, request);

        Assert.Contains(
            "ORDER BY \"t0\".\"Name\" DESC, \"t0\".\"Id\" ASC",
            plan.CommandText,
            StringComparison.Ordinal);

        Assert.Equal(2, plan.Pagination!.CursorValues.Count);
        Assert.Equal(new FieldId(2), plan.Pagination.CursorValues[0].FieldId);
        Assert.Equal(new FieldId(1), plan.Pagination.CursorValues[1].FieldId);
    }

    private static SqlPlan Compile(
        SemanticModel model,
        IMetadataProvider metadata,
        SemanticRequest request)
    {
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(
            new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);

        return new SqlCompiler(metadata).Compile(new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        });
    }

    private static SemanticRequest BuildRequest(int limit, string? after = null) => new(
        BankingModel.Customer,
        [
            new SemanticSelection(new FieldId(1), null, []),
            new SemanticSelection(new FieldId(2), null, [])
        ],
        new SemanticQueryOptions(
            Order: [new SemanticOrderTerm(new FieldId(2), SemanticSortDirection.Desc)],
            Limit: limit,
            After: after));

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL);
                              INSERT INTO "Customer" VALUES (1, 'Alice');
                              INSERT INTO "Customer" VALUES (2, 'Bob');
                              INSERT INTO "Customer" VALUES (3, 'Bob');
                              INSERT INTO "Customer" VALUES (4, 'Carol');
                              """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedDuplicateNamesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL);
                              INSERT INTO "Customer" VALUES (1, 'Alice');
                              INSERT INTO "Customer" VALUES (2, 'Bob');
                              INSERT INTO "Customer" VALUES (3, 'Bob');
                              INSERT INTO "Customer" VALUES (4, 'Carol');
                              """;
        await command.ExecuteNonQueryAsync();
    }
}