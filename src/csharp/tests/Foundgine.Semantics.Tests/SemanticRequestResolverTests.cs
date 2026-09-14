using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticRequestResolverTests
{
    [Fact]
    public void Request_resolves_to_customer_account_transaction_graph()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var transaction = new EntityId(3);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Balance", typeof(decimal))
                .Relationship(new RelationshipId(2), "Transactions", transaction, RelationshipCardinality.Many))
            .Entity(transaction, "Transaction", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Amount", typeof(decimal)))
            .Build();

        var request = new SemanticRequest(
            customer,
            [
                new SemanticSelection(
                    new FieldId(1),
                    null,
                    []),
                new SemanticSelection(
                    null,
                    new RelationshipId(1),
                    [
                        new SemanticSelection(new FieldId(1), null, []),
                        new SemanticSelection(
                            null,
                            new RelationshipId(2),
                            [
                                new SemanticSelection(new FieldId(1), null, []),
                                new SemanticSelection(new FieldId(3), null, [])
                            ])
                    ])
            ]);

        var graph = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);

        Assert.Equal(3, graph.Nodes.Count);

        Assert.Equal(customer, graph.Nodes[0].EntityId);
        Assert.Equal(new[] { new FieldId(1) }, graph.Nodes[0].Fields);

        Assert.Equal(account, graph.Nodes[1].EntityId);
        Assert.Equal(new RelationshipId(1), graph.Nodes[1].ViaRelationship);
        Assert.Equal(0, graph.Nodes[1].ParentId);
        Assert.Equal(new[] { new FieldId(1) }, graph.Nodes[1].Fields);

        Assert.Equal(transaction, graph.Nodes[2].EntityId);
        Assert.Equal(new RelationshipId(2), graph.Nodes[2].ViaRelationship);
        Assert.Equal(1, graph.Nodes[2].ParentId);
        Assert.Equal(new[] { new FieldId(1), new FieldId(3) }, graph.Nodes[2].Fields);
    }

    [Fact]
    public void Request_cannot_select_unknown_relationship()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        var request = new SemanticRequest(
            customer,
            [new SemanticSelection(null, new RelationshipId(99), [])]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request));

        Assert.Contains("does not declare relationship", ex.Message);
    }
}