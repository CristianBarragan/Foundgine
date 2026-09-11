using Foundgine.Core.Abstractions;
using Foundgine.Providers.Aot;
using Foundgine.Core.Execution;
using Foundgine.Generated;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

[FoundgineEntity(Id = 301, StorageName = "AotCustomers")]
public sealed class AotCustomer
{
    [FoundgineField(Id = 1, StorageName = "Id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(Id = 2, StorageName = "Name")]
    public string Name { get; init; } = string.Empty;

    [FoundgineRelationship(typeof(AotAccount), "CustomerId", "Id", Id = 301, Name = "Accounts")]
    public IReadOnlyList<AotAccount> Accounts { get; init; } = [];
}

[FoundgineEntity(Id = 302, StorageName = "AotAccounts")]
public sealed class AotAccount
{
    [FoundgineField(Id = 1, StorageName = "Id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(Id = 2, StorageName = "CustomerId")]
    public int CustomerId { get; init; }

    [FoundgineField(Id = 3, StorageName = "Balance")]
    public decimal Balance { get; init; }
}

public sealed class AotSqlPipelineTests
{
    [Fact]
    public async Task Generated_metadata_drives_the_existing_existing_pipeline()
    {
        var model = BuildSemanticModel();
        var request = new SemanticRequest(
            new EntityId(301),
            [
                new SemanticSelection(new FieldId(1), null, []),
                new SemanticSelection(new FieldId(2), null, []),
                new SemanticSelection(null, new RelationshipId(301),
                [
                    new SemanticSelection(new FieldId(1), null, []),
                    new SemanticSelection(new FieldId(3), null, [])
                ])
            ]);

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };

        // The AOT path must not require hand-built relational metadata.
        var sqlPlan = new SqlCompiler(GeneratedMetadata.Registry).Compile(plan);

        Assert.Contains("AotCustomers", sqlPlan.CommandText, StringComparison.Ordinal);
        Assert.Contains("AotAccounts", sqlPlan.CommandText, StringComparison.Ordinal);
        Assert.Contains("CustomerId", sqlPlan.CommandText, StringComparison.Ordinal);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);

        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(sqlPlan, new ExecutionContext());

        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Rows, row => Equals(row.Values["__fg_0_Name"], "Ada"));
        Assert.Contains(result.Rows, row => Equals(row.Values["__fg_0_Name"], "Grace"));
    }

    private static SemanticModel BuildSemanticModel()
    {
        return new SemanticModelBuilder()
            .Entity(new EntityId(301), "AotCustomer", entity => entity
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(1), "Id", typeof(int))
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(301), "Accounts", new EntityId(302), RelationshipCardinality.Many))
            .Entity(new EntityId(302), "AotAccount", entity => entity
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(1), "Id", typeof(int))
                .Field(new FieldId(2), "CustomerId", typeof(int))
                .Field(new FieldId(3), "Balance", typeof(decimal)))
            .Build();
    }

    private static async Task SeedAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "AotCustomers" (
                                  "Id" INTEGER PRIMARY KEY,
                                  "Name" TEXT NOT NULL
                              );
                              CREATE TABLE "AotAccounts" (
                                  "Id" INTEGER PRIMARY KEY,
                                  "CustomerId" INTEGER NOT NULL,
                                  "Balance" DECIMAL NOT NULL
                              );
                              INSERT INTO "AotCustomers" VALUES (1, 'Ada');
                              INSERT INTO "AotCustomers" VALUES (2, 'Grace');
                              INSERT INTO "AotAccounts" VALUES (10, 1, 100.0);
                              INSERT INTO "AotAccounts" VALUES (20, 2, 250.0);
                              """;
        await command.ExecuteNonQueryAsync();
    }
}