using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticOperationIrTests
{
    [Fact]
    public void Compiler_PreservesSemanticTopologyWithoutProviderInformation()
    {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var orders = new RelationshipId(10);
        var customerId = new FieldId(11);
        var orderId = new FieldId(21);

        var graph = new SemanticGraph();
        var root = graph.AddRoot(customer, [customerId]);
        graph.Add(order, orders, root, [orderId]);

        var operation = SemanticOperationCompiler.Compile(graph);

        Assert.Equal(customer, operation.Root.EntityId);
        Assert.Equal([customerId], operation.Root.Fields);
        Assert.Single(operation.Root.Children);

        var child = operation.Root.Children[0];
        Assert.Equal(order, child.EntityId);
        Assert.Equal(orders, child.ViaRelationship);
        Assert.Null(child.ViaConnection);
        Assert.Equal([orderId], child.Fields);
    }

    [Fact]
    public void Compiler_PreservesRootQueryOptions()
    {
        var graph = new SemanticGraph();
        graph.GetType().GetProperty(nameof(SemanticGraph.Options))!
            .SetValue(graph, new Foundgine.Core.Semantic.Query.SemanticQueryOptions(Limit: 25));
        graph.AddRoot(new EntityId(1));

        var operation = SemanticOperationCompiler.Compile(graph);

        Assert.NotNull(operation.Root.QueryOptions);
        Assert.Equal(25, operation.Root.QueryOptions!.Limit);
    }

    [Fact]
    public void Compiler_PreservesAuthorizationAsSemanticConstraint()
    {
        var entity = new EntityId(1);
        var authorization = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.Parameter("customer"), "tenantId"),
            AuthorizationPredicate.ContextParameter("tenantId"));

        var graph = new SemanticGraph();
        graph.AddRoot(entity, authorization: authorization);

        var operation = SemanticOperationCompiler.Compile(graph);

        Assert.Equal(authorization, operation.Root.Authorization);
    }

    [Fact]
    public void Compiler_RejectsMultipleRoots()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1));
        graph.AddRoot(new EntityId(2));

        var error = Assert.Throws<InvalidOperationException>(() => SemanticOperationCompiler.Compile(graph));

        Assert.Contains("exactly one root", error.Message);
    }
}