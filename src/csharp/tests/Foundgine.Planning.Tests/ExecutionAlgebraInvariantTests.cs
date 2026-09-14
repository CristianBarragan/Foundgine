using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class ExecutionAlgebraInvariantTests
{
    [Fact]
    public void Root_Uses_Scan_And_Carries_Query_Clauses()
    {
        var graph = new SemanticGraph
        {
            Options = new SemanticQueryOptions(
                Limit: 10,
                Offset: 2)
        };

        graph.AddRoot(new EntityId(1), [new FieldId(1)]);

        var plan = new Planner().Plan(graph);

        Assert.Equal(ExecutionOperation.Scan, plan.Root.Operation);
        Assert.Equal(10, plan.Root.QueryOptions!.Limit);
        Assert.Equal(2, plan.Root.QueryOptions.Offset);
        Assert.Empty(plan.Root.Children);
    }

    [Fact]
    public void Relationship_Traversal_Is_A_Logical_Operation_Not_A_Provider_Operation()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(1)]);
        var child = graph.Add(
            new EntityId(2),
            new RelationshipId(7),
            root,
            [new FieldId(2)]);

        var plan = new Planner().Plan(graph);

        Assert.Equal(ExecutionOperation.Scan, plan.Root.Operation);
        Assert.Single(plan.Root.Children);
        Assert.Equal(ExecutionOperation.Traverse, plan.Root.Children[0].Operation);
        Assert.Equal(new RelationshipId(7), plan.Root.Children[0].ViaRelationship);
        Assert.Null(plan.Root.Children[0].ViaConnection);
    }

    [Fact]
    public void Connection_Traversal_Is_Separate_From_Relationship_Traversal()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(1)]);
        graph.AddConnection(
            new EntityId(2),
            new ConnectionId(9),
            root,
            [new FieldId(2)]);

        var plan = new Planner().Plan(graph);

        var child = Assert.Single(plan.Root.Children);
        Assert.Equal(ExecutionOperation.TraverseConnection, child.Operation);
        Assert.Equal(new ConnectionId(9), child.ViaConnection);
        Assert.Null(child.ViaRelationship);
    }

    [Fact]
    public void Authorization_Is_Part_Of_The_Logical_Plan()
    {
        var graph = new SemanticGraph();
        var authorization = AuthorizationPredicate.Equal(
            AuthorizationPredicate.ContextParameter("TenantId"),
            AuthorizationPredicate.Constant("42"));

        graph.AddRoot(
            new EntityId(1),
            [new FieldId(1)],
            authorization);

        var plan = new Planner().Plan(graph);

        Assert.Same(authorization, plan.Root.Authorization);
    }

    [Fact]
    public void Query_Clauses_Are_Not_Encoded_As_Execution_Operations()
    {
        var graph = new SemanticGraph
        {
            Options = new SemanticQueryOptions(
                Filter: new SemanticFieldFilter(
                    new FieldId(1),
                    SemanticFilterOperator.Eq,
                    42),
                Limit: 5)
        };

        graph.AddRoot(new EntityId(1), [new FieldId(1)]);

        var plan = new Planner().Plan(graph);

        Assert.Equal(ExecutionOperation.Scan, plan.Root.Operation);
        Assert.NotNull(plan.Root.QueryOptions);
        Assert.NotNull(plan.Root.QueryOptions!.Filter);
        Assert.Equal(5, plan.Root.QueryOptions.Limit);
    }

    [Fact]
    public void Frozen_read_algebra_contains_only_structural_operations()
    {
        var operations = Enum.GetValues<ExecutionOperation>();

        Assert.Equal(3, operations.Length);
        Assert.Contains(ExecutionOperation.Scan, operations);
        Assert.Contains(ExecutionOperation.Traverse, operations);
        Assert.Contains(ExecutionOperation.TraverseConnection, operations);
    }
}