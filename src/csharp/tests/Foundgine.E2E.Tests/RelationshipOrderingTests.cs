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

public sealed class RelationshipOrderingTests
{
    private static readonly EntityId Customer = new(101);
    private static readonly EntityId Profile = new(102);
    private static readonly RelationshipId CustomerProfile = new(101);

    [Fact]
    public async Task To_one_relationship_order_paginates_with_compound_cursor()
    {
        var model = BuildModel();
        var metadata = BuildMetadata();
        var providerConnection = new SqliteConnection("Data Source=:memory:");
        await providerConnection.OpenAsync();
        await SeedAsync(providerConnection);

        var first = BuildRequest(1);
        var firstPlan = Compile(model, metadata, first);
        Assert.Contains("ORDER BY \"t1\".\"DisplayName\" DESC, \"t0\".\"Id\" ASC", firstPlan.CommandText,
            StringComparison.Ordinal);
        Assert.Contains("LIMIT @__fg_limit", firstPlan.CommandText, StringComparison.Ordinal);

        var provider = new SqlExecutionProvider(providerConnection);
        var firstResult = await provider.ExecuteAsync(firstPlan, PaginationExecutionContext.Create(1));

        Assert.Single(firstResult.Rows);
        Assert.Equal("Zoe", firstResult.Rows[0].Values["__fg_1_DisplayName"]);
        Assert.True(firstResult.PageInfo!.HasNextPage);
        Assert.NotNull(firstResult.PageInfo.EndCursor);

        var second = BuildRequest(1, firstResult.PageInfo.EndCursor);
        var secondPlan = Compile(model, metadata, second);
        Assert.Contains("\"t1\".\"DisplayName\" < @p0", secondPlan.CommandText, StringComparison.Ordinal);
        Assert.Contains("\"t1\".\"DisplayName\" =", secondPlan.CommandText, StringComparison.Ordinal);
        Assert.Contains("\"t0\".\"Id\" >", secondPlan.CommandText, StringComparison.Ordinal);
        Assert.Contains(" AND ", secondPlan.CommandText, StringComparison.Ordinal);

        var secondResult = await provider.ExecuteAsync(secondPlan,
            PaginationExecutionContext.Create(1, firstResult.PageInfo.EndCursor));
        Assert.Single(secondResult.Rows);
        Assert.Equal("Alice", secondResult.Rows[0].Values["__fg_1_DisplayName"]);
        Assert.False(secondResult.PageInfo!.HasNextPage);
    }

    private static SemanticModel BuildModel() =>
        new SemanticModelBuilder()
            .Entity(Customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(CustomerProfile, "Profile", Profile, RelationshipCardinality.One))
            .Entity(Profile, "Profile", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "DisplayName", typeof(string)))
            .Build();

    private static MetadataRegistry BuildMetadata()
    {
        var customer = new EntityMetadata(
            Customer,
            "Customer",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "Name"),
                new ColumnMetadata(new ColumnId(3), "ProfileId")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int), new ColumnReference(Customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(Customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Customer, new ColumnId(1)));

        var profile = new EntityMetadata(
            Profile,
            "Profile",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "DisplayName")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int), new ColumnReference(Profile, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "DisplayName", typeof(string),
                    new ColumnReference(Profile, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Profile, new ColumnId(1)));

        var relationship = new RelationshipMetadata(
            CustomerProfile,
            Customer,
            Profile,
            "Profile",
            new ColumnReference(Customer, new ColumnId(3)),
            new ColumnReference(Profile, new ColumnId(1)));

        var registry = new MetadataRegistry();
        registry.Register(customer);
        registry.Register(profile);
        registry.Register(relationship);
        return registry;
    }

    private static SemanticRequest BuildRequest(int limit, string? after = null) => new(
        Customer,
        [
            new SemanticSelection(new FieldId(1), null, []),
            new SemanticSelection(
                null,
                CustomerProfile,
                [new SemanticSelection(new FieldId(2), null, [])])
        ],
        new SemanticQueryOptions(
            Order: [new SemanticOrderTerm(new FieldId(2), SemanticSortDirection.Desc, [CustomerProfile])],
            Limit: limit,
            After: after));

    private static SqlPlan Compile(
        SemanticModel model,
        IMetadataProvider metadata,
        SemanticRequest request)
    {
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        return new SqlCompiler(metadata).Compile(new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        });
    }

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Profile" ("Id" INTEGER PRIMARY KEY, "DisplayName" TEXT NOT NULL);
                              CREATE TABLE "Customer" ("Id" INTEGER PRIMARY KEY, "Name" TEXT NOT NULL, "ProfileId" INTEGER NOT NULL);
                              INSERT INTO "Profile" VALUES (1, 'Alice');
                              INSERT INTO "Profile" VALUES (2, 'Zoe');
                              INSERT INTO "Customer" VALUES (1, 'Customer A', 1);
                              INSERT INTO "Customer" VALUES (2, 'Customer B', 2);
                              """;
        await command.ExecuteNonQueryAsync();
    }
}