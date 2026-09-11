using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.IR.Graph;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class OpenIntentTraversalTests
{
    private static readonly EntityId Customer = new(1);
    private static readonly EntityId CustomerRelationship = new(2);
    private static readonly EntityId Contract = new(3);
    private static readonly EntityId Transaction = new(4);
    private static readonly FieldId CustomerId = new(1);
    private static readonly FieldId TransactionAmount = new(2);
    private static readonly RelationshipId CustomerRelationships = new(10);
    private static readonly RelationshipId RelationshipContract = new(11);
    private static readonly RelationshipId ContractTransactions = new(12);

    [Fact]
    public void Dynamic_logical_traversal_expands_to_real_relationship_chain()
    {
        var model = BuildModel();
        var intent = new ReadIntent(
            "Customer",
            [
                new ReadSelection(
                    Relationship: "transactions",
                    Children: [new ReadSelection(Field: "Amount")])
            ]);

        var request = new ReadIntentCompiler(model).Compile(intent);
        var graph = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);

        Assert.Equal(4, graph.Nodes.Count);
        Assert.Equal(Customer, graph.Nodes[0].EntityId);
        Assert.Equal(CustomerRelationship, graph.Nodes[1].EntityId);
        Assert.Equal(Contract, graph.Nodes[2].EntityId);
        Assert.Equal(Transaction, graph.Nodes[3].EntityId);
        Assert.Equal(CustomerRelationships, graph.Nodes[1].ViaRelationship);
        Assert.Equal(RelationshipContract, graph.Nodes[2].ViaRelationship);
        Assert.Equal(ContractTransactions, graph.Nodes[3].ViaRelationship);
        Assert.Contains(TransactionAmount, graph.Nodes[3].Fields);
    }

    [Fact]
    public void Dynamic_intent_converges_on_the_same_canonical_operation_graph_as_typed_semantic_request()
    {
        var model = BuildModel();
        var snapshot = model.Freeze().CreateSnapshot();
        var intent = new ReadIntent(
            "Customer",
            [
                new ReadSelection(
                    Relationship: "transactions",
                    Children: [new ReadSelection(Field: "Amount")])
            ]);

        var dynamicGraph = new ReadIntentCompiler(snapshot).CompileOperationGraph(intent);
        var typedRequest = new ReadIntentCompiler(snapshot).Compile(intent);
        var typedGraph = SemanticOperationGraph.Create(
            SemanticOperationCompiler.Compile(
                new SemanticRequestResolver(snapshot).Resolve(typedRequest)));

        Assert.Equal(
            SemanticOperationGraphFingerprint.Create(typedGraph),
            SemanticOperationGraphFingerprint.Create(dynamicGraph));
        Assert.Equal(snapshot.ContractFingerprint, new ReadIntentCompiler(snapshot).ContractFingerprint);
        Assert.Equal(4, dynamicGraph.Nodes.Count);
        Assert.Equal(Transaction, dynamicGraph.GetNode(3).EntityId);
        Assert.Equal(TransactionAmount, dynamicGraph.GetNode(3).Fields.Single());
    }

    [Fact]
    public void Dynamic_intent_is_resolved_against_the_frozen_contract_before_planning()
    {
        var model = BuildModel();
        var snapshot = model.Freeze().CreateSnapshot();
        var intent = new ReadIntent(
            "Customer",
            [
                new ReadSelection(
                    Relationship: "transactions",
                    Children: [new ReadSelection(Field: "Amount")])
            ]);

        var graph = new ReadIntentCompiler(snapshot).CompileOperationGraph(intent);
        var planner = new Foundgine.Core.Semantic.Planning.Planner();
        var plan = planner.Plan(graph);

        Assert.Equal(Customer, plan.Root.EntityId);
        Assert.Equal(4, plan.Root.Children.Count == 1 ? CountPlanNodes(plan.Root) : -1);
        Assert.Equal(Transaction, FindPlanNode(plan.Root, Transaction).EntityId);
    }

    private static int CountPlanNodes(Foundgine.Core.Semantic.Planning.SemanticPlanNode node) =>
        1 + node.Children.Sum(CountPlanNodes);

    private static Foundgine.Core.Semantic.Planning.SemanticPlanNode FindPlanNode(
        Foundgine.Core.Semantic.Planning.SemanticPlanNode node,
        EntityId entityId)
    {
        if (node.EntityId == entityId)
            return node;

        foreach (var child in node.Children)
        {
            try
            {
                return FindPlanNode(child, entityId);
            }
            catch (Xunit.Sdk.XunitException)
            {
            }
        }

        throw new Xunit.Sdk.XunitException($"Entity '{entityId}' was not found in the plan.");
    }

    [Fact]
    public void Dynamic_logical_traversal_preserves_authorization_at_every_hop()
    {
        var model = BuildModel();
        var request = new ReadIntent(
            "Customer",
            [
                new ReadSelection(
                    Relationship: "transactions",
                    Children: [new ReadSelection(Field: "Amount")])
            ]);

        var graph = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(
            new ReadIntentCompiler(model).Compile(request));

        var authorized = new SemanticAuthorizer(new DenyContractPolicy()).Authorize(graph);

        Assert.Equal(2, authorized.Nodes.Count);
        Assert.Equal(Customer, authorized.Nodes[0].EntityId);
        Assert.Equal(CustomerRelationship, authorized.Nodes[1].EntityId);
        Assert.DoesNotContain(authorized.Nodes, x => x.EntityId == Contract);
        Assert.DoesNotContain(authorized.Nodes, x => x.EntityId == Transaction);
    }

    [Fact]
    public void Dynamic_logical_traversal_can_be_used_inside_relationship_filter()
    {
        var model = BuildModel();
        var intent = new ReadIntent(
            "Customer",
            [new ReadSelection(Field: "Id")],
            new ReadRelationshipFilter(
                "transactions",
                SemanticRelationshipQuantifier.Some,
                new ReadFieldFilter("Amount", SemanticFilterOperator.Eq, 100)));

        var request = new ReadIntentCompiler(model).Compile(intent);
        var filter = Assert.IsType<Foundgine.Core.Semantic.Query.SemanticRelationshipFilter>(request.Options!.Filter);
        var second = Assert.IsType<Foundgine.Core.Semantic.Query.SemanticRelationshipFilter>(filter.Predicate);
        var third = Assert.IsType<Foundgine.Core.Semantic.Query.SemanticRelationshipFilter>(second.Predicate);

        Assert.Equal(CustomerRelationships, filter.Relationship);
        Assert.Equal(RelationshipContract, second.Relationship);
        Assert.Equal(ContractTransactions, third.Relationship);
    }


    [Fact]
    public void Logical_traversal_is_part_of_the_semantic_model_version()
    {
        var baseModel = BuildModel();
        var routedModel = new SemanticModelBuilder()
            .Entity(Customer, "Customer", e => e.Identity(CustomerId, "Id"))
            .Entity(CustomerRelationship, "CustomerRelationship", e => e.Identity(new FieldId(3), "Id"))
            .Entity(Contract, "Contract", e => e.Identity(new FieldId(4), "Id"))
            .Entity(Transaction, "Transaction",
                e => e.Identity(new FieldId(5), "Id").Field(TransactionAmount, "Amount", typeof(decimal)))
            .Relationship<CustomerModel, CustomerRelationshipModel>(Customer, CustomerRelationships, "relationships",
                x => x.Id, CustomerRelationship, x => x.CustomerId, RelationshipCardinality.Many)
            .Relationship<CustomerRelationshipModel, ContractModel>(CustomerRelationship, RelationshipContract,
                "contract", x => x.ContractId, Contract, x => x.Id, RelationshipCardinality.One)
            .Relationship<ContractModel, TransactionModel>(Contract, ContractTransactions, "transactions", x => x.Id,
                Transaction, x => x.ContractId, RelationshipCardinality.Many)
            .Traversal(Customer, "payments", CustomerRelationships, RelationshipContract, ContractTransactions)
            .Build();

        Assert.NotEqual(
            SemanticVersionSet.For(baseModel).SemanticModelVersion,
            SemanticVersionSet.For(routedModel).SemanticModelVersion);
    }

    [Fact]
    public void Logical_traversal_is_discoverable_without_hiding_the_underlying_security_contract()
    {
        var model = BuildModel();
        var contract = SemanticCapabilityContractDiscovery.Describe(
            model,
            new AllowAllSemanticAuthorizationPolicy());

        var capability = Assert.Single(contract.Capabilities, x => x.Id == "Customer.transactions.traverse");
        Assert.Equal(Transaction, capability.TargetEntityId);
        Assert.Contains("semantic-path", capability.Constraints.Select(x => x.Name));
    }

    private static SemanticModel BuildModel() => new SemanticModelBuilder()
        .Entity(Customer, "Customer", e => e
            .Identity(CustomerId, "Id"))
        .Entity(CustomerRelationship, "CustomerRelationship", e => e
            .Identity(new FieldId(3), "Id"))
        .Entity(Contract, "Contract", e => e
            .Identity(new FieldId(4), "Id"))
        .Entity(Transaction, "Transaction", e => e
            .Identity(new FieldId(5), "Id")
            .Field(TransactionAmount, "Amount", typeof(decimal)))
        .Relationship<CustomerModel, CustomerRelationshipModel>(Customer, CustomerRelationships, "relationships",
            x => x.Id, CustomerRelationship, x => x.CustomerId, RelationshipCardinality.Many)
        .Relationship<CustomerRelationshipModel, ContractModel>(CustomerRelationship, RelationshipContract, "contract",
            x => x.ContractId, Contract, x => x.Id, RelationshipCardinality.One)
        .Relationship<ContractModel, TransactionModel>(Contract, ContractTransactions, "transactions", x => x.Id,
            Transaction, x => x.ContractId, RelationshipCardinality.Many)
        .Traversal(Customer, "transactions", CustomerRelationships, RelationshipContract, ContractTransactions)
        .Build();

    private sealed record CustomerModel(int Id);

    private sealed record CustomerRelationshipModel(int Id, int CustomerId, int ContractId);

    private sealed record ContractModel(int Id);

    private sealed record TransactionModel(int Id, int ContractId, decimal Amount);

    private sealed class DenyContractPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => entityId != Contract;
    }
}