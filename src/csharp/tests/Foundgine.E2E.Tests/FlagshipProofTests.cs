using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Providers.Storage.InMemory;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Data.Sqlite;
using Xunit;
using Foundgine.E2E.Tests.Banking;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// The single repository proof of the Foundgine thesis: a semantic intent is
/// resolved and authorized once, compiled into one provider-independent plan,
/// and then lowered independently to SQL and CLR-backed execution.
/// </summary>
public sealed class FlagshipProofTests
{
    [Fact]
    public async Task One_semantic_intent_crosses_authorization_planning_and_two_providers()
    {
        const string json = """
                            {
                              "rootEntity": "Customer",
                              "selections": [
                                { "field": "Id" },
                                { "field": "Name" }
                              ],
                              "filter": {
                                "kind": "field",
                                "field": "Name",
                                "operator": "Eq",
                                "value": "Alice"
                              },
                              "order": [
                                { "field": "Name", "direction": "Asc" }
                              ],
                              "limit": 1
                            }
                            """;

        var model = BankingSemanticModel.Build();
        var metadata = BankingRelationalMetadata.Build();
        var intent = new JsonReadIntentAdapter().Parse(json);
        var request = new ReadIntentCompiler(model).Compile(intent);

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new CustomerReadPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };

        // The logical plan contains no storage names, SQL, aliases, or provider state.
        var fingerprint = SemanticPlanFingerprint.CreateShapeKey(plan);
        Assert.False(string.IsNullOrWhiteSpace(fingerprint));
        Assert.Equal(plan.Root.EntityId, BankingSemanticModel.Customer);
        Assert.Equal([new FieldId(1), new FieldId(2)], plan.Root.Fields);

        var sqlPlan = new SqlCompiler(metadata).Compile(plan);
        Assert.Contains("SELECT", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sqlPlan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foundgine.Providers.Storage.InMemory", sqlPlan.CommandText, StringComparison.Ordinal);

        var memoryPlan = new InMemoryCompiler().Compile(plan);
        Assert.Equal("in-memory", memoryPlan.Provider);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedSqlAsync(connection);

        var sqlResult = await new SqlExecutionProvider(connection).ExecuteAsync(
            sqlPlan,
            PaginationExecutionContext.Create(1));

        var memoryData = new InMemoryDataSet()
            .Add(new InMemoryRow(BankingSemanticModel.Customer,
                new Dictionary<FieldId, object?>
                {
                    [new FieldId(1)] = 1,
                    [new FieldId(2)] = "Alice",
                    [new FieldId(5)] = 7
                }))
            .Add(new InMemoryRow(BankingSemanticModel.Customer,
                new Dictionary<FieldId, object?>
                {
                    [new FieldId(1)] = 2,
                    [new FieldId(2)] = "Bob",
                    [new FieldId(5)] = 9
                }));

        var memoryResult = await new InMemoryExecutionProvider(metadata, memoryData)
            .ExecuteAsync(memoryPlan, new ExecutionContext());

        Assert.Single(sqlResult.Rows);
        Assert.Single(memoryResult.Rows);
        Assert.Equal("Alice", sqlResult.Rows[0].Values["__fg_0_Name"]);
        Assert.Equal("Alice", memoryResult.Rows[0].EffectiveCells.Values.Single(x => Equals(x, "Alice")));

        // Both providers consumed the same logical plan and produced the same semantic result.
        Assert.Equal("Alice", memoryResult.Rows[0].EffectiveCells.Values.Single(x => x is string));
        var compiledMemoryPlan = Assert.IsType<InMemoryPlan>(memoryPlan);
        Assert.Equal(plan.Root.EntityId, compiledMemoryPlan.IR.Root.EntityId);
        Assert.Equal(plan.Root.Fields, compiledMemoryPlan.IR.Root.Fields);
    }

    private static async Task SeedSqlAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE "Customer" (
                                  "Id" INTEGER PRIMARY KEY,
                                  "Name" TEXT NOT NULL,
                                  "TenantId" INTEGER NOT NULL
                              );
                              INSERT INTO "Customer" VALUES (1, 'Alice', 7);
                              INSERT INTO "Customer" VALUES (2, 'Bob', 9);
                              """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class CustomerReadPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => entityId == BankingSemanticModel.Customer;

        public override bool CanAccessField(EntityId entityId, FieldId fieldId) =>
            entityId == BankingSemanticModel.Customer &&
            fieldId == new FieldId(1) || fieldId == new FieldId(2);
    }
}