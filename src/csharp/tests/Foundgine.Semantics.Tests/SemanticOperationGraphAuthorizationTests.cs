using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.IR.Graph;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticOperationGraphAuthorizationTests
{
    [Fact]
    public void Graph_authorization_returns_only_authorized_subgraph()
    {
        var model = CreateModel();
        var contract = model.CreateSnapshot();
        var operation = CreateOperation();
        var graph = SemanticOperationGraph.Create(operation);

        var authorized = new SemanticAuthorizer(new DenyTransactionsPolicy())
            .Authorize(contract, graph);

        Assert.Single(authorized.Nodes);
        Assert.DoesNotContain(authorized.Nodes, n => n.EntityId == EntityId.Create("Transaction"));
        Assert.Equal(authorized.RootId, authorized.Root.Id);
    }

    [Fact]
    public void Graph_authorization_evidence_is_bound_to_contract()
    {
        var model = CreateModel();
        var contract = model.CreateSnapshot();
        var result = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy())
            .AuthorizeGraphWithEvidence(contract, SemanticOperationGraph.Create(CreateOperation()));

        result.EnsureMatches(contract);
        Assert.Equal(contract.ContractFingerprint, result.Evidence.ContractFingerprint);
    }

    private static SemanticModel CreateModel()
    {
        var customerId = EntityId.Create("Customer");
        var transactionId = EntityId.Create("Transaction");
        var transactionsId = RelationshipId.Create("Customer", "Transactions");

        return new SemanticModelBuilder()
            .Entity(customerId, "Customer", e => e
                .Identity(FieldId.Create("Customer", "Id"), "Id")
                .Field(FieldId.Create("Customer", "Id"), "Id", typeof(long))
                .Relationship(transactionsId, "Transactions", transactionId, RelationshipCardinality.Many))
            .Entity(transactionId, "Transaction", e => e
                .Identity(FieldId.Create("Transaction", "Id"), "Id")
                .Field(FieldId.Create("Transaction", "Id"), "Id", typeof(long)))
            .Build()
            .Freeze();
    }

    private static SemanticOperation CreateOperation()
    {
        var transaction = new SemanticReadNode(
            2,
            EntityId.Create("Transaction"),
            new[] { FieldId.Create("Transaction", "Id") },
            RelationshipId.Create("Customer", "Transactions"),
            null,
            Array.Empty<SemanticReadNode>(),
            null,
            null);

        var customer = new SemanticReadNode(
            1,
            EntityId.Create("Customer"),
            new[] { FieldId.Create("Customer", "Id") },
            null,
            null,
            new[] { transaction },
            null,
            null);

        return new SemanticOperation(customer);
    }

    private sealed class DenyTransactionsPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) =>
            relationshipId != RelationshipId.Create("Customer", "Transactions");
    }
}