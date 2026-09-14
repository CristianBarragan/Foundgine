using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class CollectionOrderingTests
{
    private static readonly EntityId Customer = new(301);
    private static readonly EntityId Account = new(302);
    private static readonly RelationshipId CustomerAccounts = new(301);

    [Fact]
    public void Translates_count_and_min_collection_ordering()
    {
        var model = new SemanticModelBuilder()
            .Entity(Customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(CustomerAccounts, "Accounts", Account, RelationshipCardinality.Many))
            .Entity(Account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Balance", typeof(decimal)))
            .Build();

        var adapter = new HotChocolateSemanticAdapter(model);
        var request = adapter.Adapt("""
                                    query {
                                      customer(order: {
                                        accounts: {
                                          _count: DESC
                                          balance: { max: ASC }
                                        }
                                      }) {
                                        id
                                      }
                                    }
                                    """);

        Assert.Equal(2, request.Options!.EffectiveOrder.Count);
        Assert.Equal(SemanticOrderAggregate.Count, request.Options.EffectiveOrder[0].Aggregate);
        Assert.Equal(SemanticOrderAggregate.Max, request.Options.EffectiveOrder[1].Aggregate);
        Assert.Equal(CustomerAccounts, request.Options.EffectiveOrder[0].EffectivePath.Single());
    }
}