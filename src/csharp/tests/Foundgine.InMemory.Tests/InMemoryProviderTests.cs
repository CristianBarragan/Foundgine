using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Providers.Storage.InMemory;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Storage.InMemory.Tests;

public sealed class InMemoryProviderTests
{
    [Fact]
    public async Task Same_execution_plan_can_execute_without_sql()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var customerId = new FieldId(1);
        var customerName = new FieldId(2);
        var accountId = new FieldId(1);
        var balance = new FieldId(2);
        var accountCustomerId = new FieldId(3);
        var accounts = new RelationshipId(1);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(customerId, "Id")
                .Field(customerName, "Name", typeof(string))
                .Relationship(accounts, "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(accountId, "Id")
                .Field(balance, "Balance", typeof(decimal))
                .Field(accountCustomerId, "CustomerId", typeof(int)))
            .Build();

        var graph = new SemanticGraph { };
        var root = graph.AddRoot(customer, [customerId, customerName]);
        graph.Add(account, accounts, root, [accountId, balance]);
        graph = graph.WithAuthorization(0,
            AuthorizationPredicate.Equal(
                AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "Id"),
                AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("tenant"), "id")));

        // The provider-neutral plan contains no SQL objects.
        var plan = new Planner().Plan(graph) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };

        var metadata = new MetadataRegistry();
        metadata.Register(Entity(customer, "Customer", customerId, customerName, 11, 12));
        metadata.Register(new EntityMetadata(
            account,
            "Account",
            [
                new ColumnMetadata(new ColumnId(21), "Id"), new ColumnMetadata(new ColumnId(22), "Balance"),
                new ColumnMetadata(new ColumnId(23), "CustomerId")
            ],
            Fields:
            [
                new FieldMetadata(accountId, "Id", typeof(int), new ColumnReference(account, new ColumnId(21))),
                new FieldMetadata(balance, "Balance", typeof(decimal), new ColumnReference(account, new ColumnId(22))),
                new FieldMetadata(accountCustomerId, "CustomerId", typeof(int),
                    new ColumnReference(account, new ColumnId(23)))
            ],
            PrimaryKey: new ColumnReference(account, new ColumnId(21))));
        metadata.Register(new RelationshipMetadata(
            accounts, customer, account, "Accounts",
            new ColumnReference(customer, new ColumnId(11)),
            new ColumnReference(account, new ColumnId(23))));

        var data = new InMemoryDataSet()
            .Add(new InMemoryRow(customer,
                new Dictionary<FieldId, object?> { [customerId] = 1, [customerName] = "Alice" }))
            .Add(new InMemoryRow(customer,
                new Dictionary<FieldId, object?> { [customerId] = 2, [customerName] = "Bob" }))
            .Add(new InMemoryRow(account,
                new Dictionary<FieldId, object?> { [accountId] = 1, [balance] = 100m, [accountCustomerId] = 1 }))
            .Add(new InMemoryRow(account,
                new Dictionary<FieldId, object?> { [accountId] = 2, [balance] = 200m, [accountCustomerId] = 1 }))
            .Add(new InMemoryRow(account,
                new Dictionary<FieldId, object?> { [accountId] = 3, [balance] = 300m, [accountCustomerId] = 2 }));

        var provider = new InMemoryExecutionProvider(metadata, data);
        var result = await provider.ExecuteAsync(
            new InMemoryCompiler().Compile(ExecutionIRCompiler.Compile(plan)),
            new ExecutionContext(new Dictionary<string, object?> { ["tenant.id"] = 1 }));

        var materialized = new ResultMaterializer(model).Materialize(plan, result);
        var alice = Assert.Single(materialized.Roots);
        Assert.Equal("Alice", alice.Values[customerName]);
        Assert.Equal(2, alice.Children[new RelationshipId(1)].Count);
    }

    [Fact]
    public void Provider_plan_is_not_a_sql_plan()
    {
        var plan = new SemanticPlan(
            new SemanticPlanNode(0, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            AuthorizationBinding: new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));

        var compiled = new InMemoryCompiler().Compile(ExecutionIRCompiler.Compile(plan));

        Assert.IsType<InMemoryPlan>(compiled);
        Assert.Equal("in-memory", compiled.Provider);
    }

    private static EntityMetadata Entity(EntityId entity, string name, FieldId id, FieldId value, int idColumn,
        int valueColumn) =>
        new(
            entity,
            name,
            [
                new ColumnMetadata(new ColumnId((ushort)idColumn), "Id"),
                new ColumnMetadata(new ColumnId((ushort)valueColumn), "Value")
            ],
            Fields:
            [
                new FieldMetadata(id, "Id", typeof(int), new ColumnReference(entity, new ColumnId((ushort)idColumn))),
                new FieldMetadata(value, "Value", typeof(string),
                    new ColumnReference(entity, new ColumnId((ushort)valueColumn)))
            ],
            PrimaryKey: new ColumnReference(entity, new ColumnId((ushort)idColumn)));
}