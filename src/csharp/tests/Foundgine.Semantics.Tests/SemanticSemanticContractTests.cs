using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticSemanticContractTests
{
    [Fact]
    public void Built_model_is_snapshot_after_builder_changes()
    {
        var builder = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)));

        var first = builder.Build();

        builder.Entity(new EntityId(2), "Account", e => e
            .Identity(new FieldId(1), "Id"));

        Assert.Single(first.Entities);
        Assert.Throws<KeyNotFoundException>(() => first.Get(new EntityId(2)));
    }

    [Fact]
    public void Semantic_field_exposes_provider_independent_type_and_capabilities()
    {
        var field = new SemanticField(
            new FieldId(2),
            "Balance",
            typeof(decimal),
            Capabilities: SemanticFieldCapabilities.Filterable | SemanticFieldCapabilities.Sortable);

        var scalar = Assert.IsType<SemanticType.Scalar>(field.EffectiveSemanticType);
        Assert.Equal(SemanticScalarKind.Decimal, scalar.Kind);
        Assert.True(field.Capabilities.HasFlag(SemanticFieldCapabilities.Filterable));
        Assert.False(field.Capabilities.HasFlag(SemanticFieldCapabilities.Aggregatable));
    }

    [Fact]
    public void Resolver_canonicalizes_count_and_adds_cursor_identity_tie_breaker()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(10), "Orders", new EntityId(2), RelationshipCardinality.Many))
            .Entity(new EntityId(2), "Order", e => e
                .Identity(new FieldId(3), "Id")
                .Field(new FieldId(4), "Total", typeof(decimal)))
            .Build();

        var request = new SemanticRequest(
            new EntityId(1),
            [new SemanticSelection(new FieldId(1), null, [])],
            new SemanticQueryOptions(
                Order:
                [
                    new SemanticOrderTerm(new FieldId(999), SemanticSortDirection.Desc, [new RelationshipId(10)],
                        SemanticOrderAggregate.Count)
                ],
                Limit: 10,
                After: "cursor"));

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var order = resolved.Options!.EffectiveOrder;

        Assert.Equal(new FieldId(3), order[0].Field);
        Assert.Equal(SemanticOrderAggregate.Count, order[0].Aggregate);
        Assert.Equal(new FieldId(1), order[1].Field);
        Assert.Empty(order[1].EffectivePath);
    }

    [Fact]
    public void Resolver_rejects_negative_query_controls_before_planning()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id"))
            .Build();

        var request = new SemanticRequest(
            new EntityId(1),
            [new SemanticSelection(new FieldId(1), null, [])],
            new SemanticQueryOptions(Limit: -1));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request));
        Assert.Contains("limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SemanticGraphValidationTests
{
    [Fact]
    public void Resolver_proves_relationship_target_consistency()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Relationship(new RelationshipId(10), "Orders", new EntityId(2), RelationshipCardinality.Many))
            .Entity(new EntityId(2), "Order", e => e.Identity(new FieldId(3), "Id"))
            .Build();

        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(1)]);
        graph.Add(new EntityId(999), new RelationshipId(10), root, [new FieldId(1)]);

        var ex = Assert.Throws<InvalidOperationException>(() => SemanticGraphValidator.Validate(graph, model));
        Assert.Contains("targets entity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}