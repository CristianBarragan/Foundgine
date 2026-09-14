using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class ConnectionPlanningTests
{
    [Fact]
    public void A_connection_is_preserved_as_a_distinct_traversal_operation()
    {
        var root = new EntityId(10);
        var target = new EntityId(20);
        var connection = new ConnectionId(30);

        var graph = new SemanticGraph();
        var rootNode = graph.AddRoot(root, [new FieldId(1)]);
        var child = graph.AddConnection(target, connection, rootNode, [new FieldId(2)]);

        var plan = new Planner().Plan(graph);

        Assert.Equal(ExecutionOperation.Scan, plan.Root.Operation);
        Assert.Single(plan.Root.Children);
        Assert.Equal(ExecutionOperation.TraverseConnection, childPlan(plan).Operation);
        Assert.Equal(connection, childPlan(plan).ViaConnection);
        Assert.Null(childPlan(plan).ViaRelationship);
        Assert.Equal(target, childPlan(plan).EntityId);
    }

    [Fact]
    public void A_connection_cannot_be_attached_to_a_root()
    {
        var graph = new SemanticGraph();

        Assert.Throws<ArgumentException>(() =>
            graph.AddConnection(
                new EntityId(20),
                new ConnectionId(30),
                null!,
                [new FieldId(2)]));
    }

    [Fact]
    public void A_node_cannot_mix_relationship_and_connection_edges()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(10));

        var node = graph.AddConnection(
            new EntityId(20),
            new ConnectionId(30),
            root);

        Assert.Null(node.ViaRelationship);
        Assert.Equal(new ConnectionId(30), node.ViaConnection);
    }

    [Fact]
    public void Authorization_predicate_is_preserved_in_the_execution_plan()
    {
        var predicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.Parameter("user"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.Parameter("contract"), "TenantId"));

        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(10));
        graph.AddConnection(new EntityId(20), new ConnectionId(30), root, authorization: predicate);

        var plan = new Planner().Plan(graph);
        var child = childPlan(plan);

        Assert.Equal(AuthorizationPredicateKind.Equal, child.Authorization!.Kind);
        Assert.Equal("TenantId", child.Authorization.Left!.Name);
        Assert.Equal("TenantId", child.Authorization.Right!.Name);
    }

    private static SemanticPlanNode childPlan(SemanticPlan plan) =>
        Assert.Single(plan.Root.Children);
}
