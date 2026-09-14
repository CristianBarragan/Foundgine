using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class RelationshipOrderingTests
{
    [Fact]
    public void To_one_relationship_order_translates_to_a_path_aware_semantic_term()
    {
        var customer = new EntityId(1);
        var profile = new EntityId(2);
        var customerProfile = new RelationshipId(1);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Relationship(customerProfile, "Profile", profile, RelationshipCardinality.One))
            .Entity(profile, "Profile", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "DisplayName", typeof(string)))
            .Build();

        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query {
                                                                     customer(order: { profile: { displayName: DESC } }) {
                                                                       id
                                                                       profile { displayName }
                                                                     }
                                                                   }
                                                                   """);

        var term = Assert.Single(request.Options!.EffectiveOrder);
        Assert.Equal(new FieldId(2), term.Field);
        Assert.Equal(SemanticSortDirection.Desc, term.Direction);
        Assert.Equal([customerProfile], term.EffectivePath);
    }

    [Fact]
    public void Collection_relationship_order_is_rejected_before_planning()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var accounts = new RelationshipId(1);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Relationship(accounts, "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Balance", typeof(decimal)))
            .Build();

        var exception = Assert.Throws<NotSupportedException>(() =>
            new HotChocolateSemanticAdapter(model).Adapt("""
                                                         query {
                                                           customer(order: { accounts: { balance: DESC } }) {
                                                             id
                                                             accounts { balance }
                                                           }
                                                         }
                                                         """));

        Assert.Contains("collection relationship", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}