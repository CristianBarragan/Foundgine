using System.Collections.ObjectModel;
using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticModelFreezeTests
{
    [Fact]
    public void Built_model_is_not_frozen_until_explicit_freeze_boundary()
    {
        var model = BuildModel();

        Assert.False(model.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => model.EnsureFrozen());
    }

    [Fact]
    public void Freeze_preserves_contract_identity()
    {
        var model = BuildModel();
        var frozen = model.Freeze();

        Assert.True(frozen.IsFrozen);
        Assert.Equal(model.ContractFingerprint, frozen.ContractFingerprint);
        Assert.Equal(model.Entities.Single().Id, frozen.Entities.Single().Id);
        Assert.Equal(model.Entities.Single().Fields.Single().Id, frozen.Entities.Single().Fields.Single().Id);
    }

    [Fact]
    public void Freezing_an_already_frozen_model_is_idempotent()
    {
        var frozen = BuildModel().Freeze();

        Assert.Same(frozen, frozen.Freeze());
        frozen.EnsureFrozen();
    }

    [Fact]
    public void Entity_field_and_relationship_collections_are_defensively_immutable()
    {
        var model = new SemanticModelBuilder()
            .Entity<TestCustomer>(EntityId.Create("Customer"), "Customer", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name))
            .Entity<TestOrder>(EntityId.Create("Order"), "Order", e => e
                .Identity(x => x.Id)
                .Field(x => x.CustomerId))
            .Relationship<TestCustomer, TestOrder>(
                EntityId.Create("Customer"),
                "Orders",
                x => x.Id,
                EntityId.Create("Order"),
                x => x.CustomerId,
                RelationshipCardinality.Many)
            .Build()
            .Freeze();

        var entity = model.ResolveEntity("Customer");

        Assert.IsType<ReadOnlyCollection<SemanticField>>(entity.Fields);
        Assert.IsType<ReadOnlyCollection<SemanticRelationship>>(entity.Relationships);
        Assert.Throws<NotSupportedException>(() => ((IList<SemanticField>)entity.Fields).Add(entity.Fields.Single()));
        Assert.Throws<NotSupportedException>(() => ((IList<SemanticRelationship>)entity.Relationships).Clear());
    }

    [Fact]
    public void Field_nested_collections_are_defensively_immutable()
    {
        var model = new SemanticModelBuilder()
            .Entity<TestCustomer>(EntityId.Create("Customer"), "Customer", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .FieldAlias(x => x.Name, "displayName")
                .Constraint(x => x.Name, SemanticConstraint.Pattern("^[A-Z].*")))
            .Build()
            .Freeze();

        var field = model.ResolveEntity("Customer").Fields.Single(x => x.Name == "Name");

        Assert.IsType<ReadOnlyCollection<SemanticAlias>>(field.Aliases);
        Assert.IsType<ReadOnlyCollection<SemanticConstraint>>(field.Constraints);
        Assert.Throws<NotSupportedException>(() => ((IList<SemanticAlias>)field.EffectiveAliases).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<SemanticConstraint>)field.EffectiveConstraints).Clear());
    }

    [Fact]
    public void Traversal_path_is_defensively_immutable()
    {
        var model = new SemanticModelBuilder()
            .Entity<TestCustomer>(EntityId.Create("Customer"), "Customer",
                e => e.Identity(x => x.Id).Field(x => x.Name))
            .Entity<TestOrder>(EntityId.Create("Order"), "Order", e => e.Identity(x => x.Id).Field(x => x.CustomerId))
            .Relationship<TestCustomer, TestOrder>(
                EntityId.Create("Customer"), "Orders", x => x.Id,
                EntityId.Create("Order"), x => x.CustomerId, RelationshipCardinality.Many)
            .Traversal("Customer", "OrdersPath", "Orders")
            .Build()
            .Freeze();

        var traversal = model.Traversals.Single();

        Assert.IsType<ReadOnlyCollection<RelationshipId>>(traversal.Path);
        Assert.Throws<NotSupportedException>(() => ((IList<RelationshipId>)traversal.Path).Clear());
    }

    private static SemanticModel BuildModel() => new SemanticModelBuilder()
        .Entity<TestCustomer>(EntityId.Create("Customer"), "Customer", e => e
            .Identity(x => x.Id)
            .Field(x => x.Name))
        .Build();

    private sealed class TestCustomer
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class TestOrder
    {
        public int Id { get; init; }
        public int CustomerId { get; init; }
    }
}