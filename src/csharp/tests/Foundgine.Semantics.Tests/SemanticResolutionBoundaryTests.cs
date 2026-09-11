using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticResolutionBoundaryTests
{
    [Fact]
    public void Empty_selection_set_is_rejected()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        var request = new SemanticRequest(new EntityId(1), []);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request));

        Assert.Contains("at least one selection", ex.Message);
    }

    [Fact]
    public void Repeated_relationship_selection_is_rejected_before_graph_construction()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var relationship = new RelationshipId(1);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Relationship(relationship, "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        var request = new SemanticRequest(
            customer,
            [
                new SemanticSelection(null, relationship, [new SemanticSelection(new FieldId(1), null, [])]),
                new SemanticSelection(null, relationship, [new SemanticSelection(new FieldId(1), null, [])])
            ]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request));

        Assert.Contains("selected more than once", ex.Message);
    }

    [Fact]
    public void Resolver_does_not_require_provider_metadata()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var request = new SemanticRequest(
            customer,
            [new SemanticSelection(new FieldId(2), null, [])]);

        var graph = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);

        Assert.Single(graph.Nodes);
        Assert.Equal(customer, graph.Nodes[0].EntityId);
        Assert.Equal(new[] { new FieldId(2) }, graph.Nodes[0].Fields);
    }
}