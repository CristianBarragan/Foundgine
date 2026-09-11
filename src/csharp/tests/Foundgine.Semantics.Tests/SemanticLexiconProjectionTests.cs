using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticLexiconProjectionTests
{
    [Fact]
    public void Build_throws_when_contract_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => SemanticLexiconProjection.Build(null!));
    }

    [Fact]
    public void Build_projects_an_entity_as_both_an_entity_and_a_node_entry_carrying_aliases()
    {
        var customer = new EntityId(1);

        var contract = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Alias("Client")
                .Identity(new FieldId(1), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var entries = SemanticLexiconProjection.Build(contract);

        var entityEntry = Assert.Single(entries, x =>
            x.Kind == SemanticLexicalCandidateKind.Entity && x.CanonicalName == "Customer");
        Assert.Equal(customer, entityEntry.EntityId);
        Assert.Contains("Client", entityEntry.EffectiveAliases);
        Assert.Equal("Customer", entityEntry.SearchText);

        var nodeEntry = Assert.Single(entries, x =>
            x.Kind == SemanticLexicalCandidateKind.Node && x.CanonicalName == "Customer");
        Assert.Equal(customer, nodeEntry.EntityId);
        Assert.Contains("Client", nodeEntry.EffectiveAliases);
    }

    [Fact]
    public void Build_projects_a_field_with_its_owning_entity_name_and_aliases()
    {
        var customer = new EntityId(1);
        var nameField = new FieldId(2);

        var contract = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(nameField, "Name", typeof(string))
                .FieldAlias(nameField, "Full Name"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var entries = SemanticLexiconProjection.Build(contract);

        var fieldEntry = Assert.Single(entries, x => x.Kind == SemanticLexicalCandidateKind.Field);
        Assert.Equal("Name", fieldEntry.CanonicalName);
        Assert.Equal(customer, fieldEntry.EntityId);
        Assert.Equal(nameField, fieldEntry.FieldId);
        Assert.Equal("Customer Name", fieldEntry.SearchText);
        Assert.Contains("Full Name", fieldEntry.EffectiveAliases);
    }

    [Fact]
    public void Build_projects_a_relationship_with_source_target_and_aliases()
    {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var relationshipId = RelationshipId.Create("Customer", "Orders");

        var contract = new SemanticModelBuilder()
            .Entity(order, "Order", e => e.Identity(new FieldId(2), "Id"))
            .Entity<Dummy>(customer, "Customer", e => e
                .Identity(x => x.Id, "Id")
                .Relationship<Dummy>(relationshipId, "Orders", x => x.Id, x => x.Id, order,
                    RelationshipCardinality.Many)
                .RelationshipAlias(relationshipId, "Purchases"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var entries = SemanticLexiconProjection.Build(contract);

        var relationshipEntry = Assert.Single(entries, x => x.Kind == SemanticLexicalCandidateKind.Relationship);
        Assert.Equal("Orders", relationshipEntry.CanonicalName);
        Assert.Equal(customer, relationshipEntry.SourceEntityId);
        Assert.Equal(order, relationshipEntry.TargetEntityId);
        Assert.Equal("Customer Orders Order", relationshipEntry.SearchText);
        Assert.Contains("Purchases", relationshipEntry.EffectiveAliases);
    }

    [Fact]
    public void Build_projects_a_logical_traversal_with_its_endpoint_names()
    {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var line = new EntityId(3);

        var contract = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Entity(order, "Order", e => e.Identity(new FieldId(2), "Id"))
            .Entity(line, "OrderLine", e => e.Identity(new FieldId(3), "Id"))
            .Relationship<Dummy, Dummy>(customer, new RelationshipId(1), "Orders", x => x.Id, order, x => x.Id,
                RelationshipCardinality.Many)
            .Relationship<Dummy, Dummy>(order, new RelationshipId(2), "Lines", x => x.Id, line, x => x.Id,
                RelationshipCardinality.Many)
            .Traversal("Customer", "PurchasedLines", "Orders", "Lines")
            .Build()
            .Freeze()
            .CreateSnapshot();

        var entries = SemanticLexiconProjection.Build(contract);

        var traversalEntry = Assert.Single(entries, x => x.Kind == SemanticLexicalCandidateKind.Traversal);
        Assert.Equal("PurchasedLines", traversalEntry.CanonicalName);
        Assert.Equal(customer, traversalEntry.SourceEntityId);
        Assert.Equal(line, traversalEntry.TargetEntityId);
        Assert.Equal("Customer PurchasedLines OrderLine", traversalEntry.SearchText);
        Assert.Null(traversalEntry.EntityId);
    }

    [Fact]
    public void Build_returns_no_entries_beyond_entity_and_node_pairs_for_a_bare_entity()
    {
        var contract = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Standalone", e => e.Identity(new FieldId(1), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var entries = SemanticLexiconProjection.Build(contract);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, x => x.Kind == SemanticLexicalCandidateKind.Entity);
        Assert.Contains(entries, x => x.Kind == SemanticLexicalCandidateKind.Node);
    }

    private sealed class Dummy
    {
        public long Id { get; set; }
    }
}