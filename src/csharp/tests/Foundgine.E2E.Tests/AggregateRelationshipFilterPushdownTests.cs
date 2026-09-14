using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
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

public sealed class AggregateRelationshipFilterPushdownTests
{
    [Fact]
    public async Task FilteredCountPreservesResultAndPushesPredicateIntoAggregateSubquery()
    {
        var model = BuildModel();
        var metadata = BuildMetadata();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var relationship = new RelationshipId(301);
        var open = new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, "open");
        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(relationship, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0),
            new SemanticRelationshipFilter(relationship, SemanticRelationshipQuantifier.Some, open)
        ]);

        var request = new SemanticRequest(
            new EntityId(301),
            [new SemanticSelection(new FieldId(1), null, [])],
            new SemanticQueryOptions(Filter: filter));

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var semanticPlan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var optimized = new SemanticPlanOptimizer().Optimize(semanticPlan);
        var plan = new SqlCompiler(metadata).Compile(optimized.Plan);

        Assert.Contains("COUNT(*)", plan.CommandText, StringComparison.Ordinal);
        Assert.Contains("Status", plan.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("EXISTS (SELECT 1", plan.CommandText, StringComparison.Ordinal);

        var result = await new SqlExecutionProvider(connection).ExecuteAsync(plan, new ExecutionContext());

        Assert.Equal(new[] { 1, 2 }, result.Rows
            .Select(r => Convert.ToInt32(r.Values["__fg_0_Id"]))
            .ToArray());
    }

    private static SemanticModel BuildModel() =>
        new SemanticModelBuilder()
            .Entity(new EntityId(301), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(301), "Accounts", new EntityId(302), RelationshipCardinality.Many))
            .Entity(new EntityId(302), "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerId", typeof(int))
                .Field(new FieldId(3), "Status", typeof(string)))
            .Build();

    private static MetadataRegistry BuildMetadata()
    {
        var customer = new EntityMetadata(
            new EntityId(301), "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int),
                    new ColumnReference(new EntityId(301), new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(new EntityId(301), new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(new EntityId(301), new ColumnId(1)));

        var account = new EntityMetadata(
            new EntityId(302), "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Status")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int),
                    new ColumnReference(new EntityId(302), new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "CustomerId", typeof(int),
                    new ColumnReference(new EntityId(302), new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Status", typeof(string),
                    new ColumnReference(new EntityId(302), new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(new EntityId(302), new ColumnId(1)));

        var relationship = new RelationshipMetadata(
            new RelationshipId(301), new EntityId(301), new EntityId(302), "Accounts",
            new ColumnReference(new EntityId(301), new ColumnId(1)),
            new ColumnReference(new EntityId(302), new ColumnId(2)));

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
                              CREATE TABLE "Account" ("Id" INTEGER PRIMARY KEY, "CustomerId" INTEGER NOT NULL, "Status" TEXT NOT NULL);
                              INSERT INTO "Customer" VALUES (1, 'A');
                              INSERT INTO "Customer" VALUES (2, 'B');
                              INSERT INTO "Customer" VALUES (3, 'C');
                              INSERT INTO "Account" VALUES (11, 1, 'open');
                              INSERT INTO "Account" VALUES (12, 1, 'closed');
                              INSERT INTO "Account" VALUES (21, 2, 'open');
                              INSERT INTO "Account" VALUES (31, 3, 'closed');
                              """;
        await command.ExecuteNonQueryAsync();
    }
}