using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class AggregateFilterTests
{
    private static readonly EntityId Customer = new(301);
    private static readonly EntityId Account = new(302);
    private static readonly RelationshipId CustomerAccounts = new(301);

    [Fact]
    public async Task Count_filter_selects_parents_with_at_least_two_children()
    {
        var model = BuildModel();
        var metadata = BuildMetadata();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var request = new SemanticRequest(
            Customer,
            [new SemanticSelection(new FieldId(1), null, [])],
            new SemanticQueryOptions(
                Filter: new SemanticAggregateFilter(
                    CustomerAccounts,
                    SemanticFilterAggregate.Count,
                    null,
                    SemanticAggregateFilterOperator.Gte,
                    2)));

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new SqlCompiler(metadata).Compile(new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        });

        Assert.Contains("COUNT(*)", plan.CommandText, StringComparison.Ordinal);
        Assert.Contains(">= @p0", plan.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("INNER JOIN", plan.CommandText, StringComparison.Ordinal);

        var result = await new SqlExecutionProvider(connection).ExecuteAsync(plan, new ExecutionContext());

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(new[] { 1, 2 }, result.Rows.Select(r => Convert.ToInt32(r.Values["__fg_0_Id"])).ToArray());
    }

    [Fact]
    public async Task Max_filter_can_compare_a_collection_field()
    {
        var model = BuildModel();
        var metadata = BuildMetadata();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var request = new SemanticRequest(
            Customer,
            [new SemanticSelection(new FieldId(1), null, [])],
            new SemanticQueryOptions(
                Filter: new SemanticAggregateFilter(
                    CustomerAccounts,
                    SemanticFilterAggregate.Max,
                    new FieldId(3),
                    SemanticAggregateFilterOperator.Gt,
                    100d)));

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new SqlCompiler(metadata).Compile(new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        });
        var result = await new SqlExecutionProvider(connection).ExecuteAsync(plan, new ExecutionContext());

        Assert.Single(result.Rows);
        Assert.Equal(2, Convert.ToInt32(result.Rows[0].Values["__fg_0_Id"]));
    }

    private static SemanticModel BuildModel() =>
        new SemanticModelBuilder()
            .Entity(Customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(CustomerAccounts, "Accounts", Account, RelationshipCardinality.Many))
            .Entity(Account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerId", typeof(int))
                .Field(new FieldId(3), "Balance", typeof(decimal)))
            .Build();

    private static MetadataRegistry BuildMetadata()
    {
        var customer = new EntityMetadata(
            Customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int), new ColumnReference(Customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(Customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Customer, new ColumnId(1)));

        var account = new EntityMetadata(
            Account, "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Balance")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int), new ColumnReference(Account, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "CustomerId", typeof(int),
                    new ColumnReference(Account, new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Balance", typeof(decimal),
                    new ColumnReference(Account, new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(Account, new ColumnId(1)));

        var relationship = new RelationshipMetadata(
            CustomerAccounts, Customer, Account, "Accounts",
            new ColumnReference(Customer, new ColumnId(1)),
            new ColumnReference(Account, new ColumnId(2)));

        var registry = new MetadataRegistry();
        registry.Register(customer);
        registry.Register(account);
        registry.Register(relationship);
        return registry;
    }

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL);
                              CREATE TABLE "Account" ("Id" INTEGER PRIMARY KEY, "CustomerId" INTEGER NOT NULL, "Balance" REAL NOT NULL);
                              INSERT INTO "Customer" VALUES (1, 'A');
                              INSERT INTO "Customer" VALUES (2, 'B');
                              INSERT INTO "Customer" VALUES (3, 'C');
                              INSERT INTO "Account" VALUES (11, 1, 10.0);
                              INSERT INTO "Account" VALUES (12, 1, 20.0);
                              INSERT INTO "Account" VALUES (21, 2, 150.0);
                              INSERT INTO "Account" VALUES (22, 2, 90.0);
                              INSERT INTO "Account" VALUES (31, 3, 50.0);
                              """;
        await command.ExecuteNonQueryAsync();
    }
}