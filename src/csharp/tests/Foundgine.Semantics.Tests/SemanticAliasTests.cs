using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticAliasTests
{
    [Fact]
    public void Entity_alias_resolves_without_changing_identity()
    {
        var id = EntityId.Create("Customer");
        var model = new SemanticModelBuilder()
            .Entity(id, "Customer", e => e
                .Alias("Client")
                .Identity(FieldId.Create("Customer", "Id"), "Id"))
            .Build();

        var entity = model.ResolveEntity("client");
        Assert.Equal(id, entity.Id);
        Assert.Equal("Customer", entity.Name);
    }

    [Fact]
    public void Field_and_relationship_aliases_resolve_to_canonical_declarations()
    {
        var customer = EntityId.Create("Customer");
        var account = EntityId.Create("Account");
        var field = FieldId.Create("Customer", "Name");
        var relationship = RelationshipId.Create("Customer", "Accounts");

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(FieldId.Create("Customer", "Id"), "Id")
                .Field(field, "Name", typeof(string))
                .FieldAlias(field, "DisplayName")
                .Relationship(relationship, "Accounts", account, RelationshipCardinality.Many)
                .RelationshipAlias(relationship, "CustomerAccounts"))
            .Entity(account, "Account", e => e.Identity(FieldId.Create("Account", "Id"), "Id"))
            .Build();

        var entity = model.Get(customer);
        Assert.Equal(field, entity.Fields.Single(x => x.EffectiveAliases.Any(a => a.Name == "DisplayName")).Id);
        Assert.Equal(relationship,
            entity.Relationships.Single(x => x.EffectiveAliases.Any(a => a.Name == "CustomerAccounts")).Id);
    }

    [Fact]
    public void Alias_collision_between_entities_is_rejected()
    {
        var first = EntityId.Create("Customer");
        var second = EntityId.Create("Account");

        Assert.Throws<InvalidOperationException>(() => new SemanticModelBuilder()
            .Entity(first, "Customer", e => e
                .Alias("Party")
                .Identity(FieldId.Create("Customer", "Id"), "Id"))
            .Entity(second, "Account", e => e
                .Alias("Party")
                .Identity(FieldId.Create("Account", "Id"), "Id"))
            .Build());
    }
}