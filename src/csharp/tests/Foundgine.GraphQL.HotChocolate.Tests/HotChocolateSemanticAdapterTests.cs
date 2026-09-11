using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class HotChocolateSemanticAdapterTests
{
    [Fact]
    public void GraphQL_query_translates_to_provider_neutral_semantic_request()
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

        const string query = """
                             query {
                               customer {
                                 id
                                 name
                                 accounts {
                                   id
                                   transactions {
                                     id
                                     amount
                                   }
                                 }
                               }
                             }
                             """;

        var request = new HotChocolateSemanticAdapter(model).Adapt(query);

        Assert.Equal(customer, request.Root);
        Assert.Equal(3, request.Selections.Count);
        Assert.Equal(new FieldId(1), request.Selections[0].Field);
        Assert.Equal(new FieldId(2), request.Selections[1].Field);

        var accounts = request.Selections[2];
        Assert.Equal(new RelationshipId(1), accounts.Relationship);
        Assert.Equal(2, accounts.Children.Count);
        Assert.Equal(new FieldId(1), accounts.Children[0].Field);

        var transactions = accounts.Children[1];
        Assert.Equal(new RelationshipId(2), transactions.Relationship);
        Assert.Equal(new[] { new FieldId(1), new FieldId(3) },
            transactions.Children.Select(x => x.Field!.Value));
    }

    [Fact]
    public void Inline_fragment_is_transparent_to_semantic_resolution()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        const string query = """
                             query {
                               customer {
                                 id
                                 ... on Customer {
                                   name
                                 }
                               }
                             }
                             """;

        var request = new HotChocolateSemanticAdapter(model).Adapt(query);

        Assert.Equal(new[] { new FieldId(1), new FieldId(2) },
            request.Selections.Select(x => x.Field!.Value));
    }

    [Fact]
    public void Unknown_graphql_field_is_rejected()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        const string query = """
                             query {
                               customer {
                                 doesNotExist
                               }
                             }
                             """;

        var ex = Assert.Throws<InvalidOperationException>(() => new HotChocolateSemanticAdapter(model).Adapt(query));

        Assert.Contains("doesNotExist", ex.Message);
    }

    [Fact]
    public void Alias_is_preserved_in_result_shape_without_entering_semantic_request()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id"))
            .Build();

        var adaptation = new HotChocolateSemanticAdapter(model).AdaptResultShape("""
            query { customer { customerId: id } }
            """);

        Assert.Equal(new FieldId(1), Assert.Single(adaptation.Request.Selections).Field);
        Assert.Equal("id", adaptation.Result.Fields[0].GraphQLName);
        Assert.Equal("customerId", adaptation.Result.Fields[0].Alias);
    }

    [Fact]
    public void Arguments_are_rejected_until_semantic_arguments_exist()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        // Root arguments are also rejected by the adapter boundary.
        const string queryWithArgument = """
                                         query {
                                           customer(id: 1) {
                                             id
                                           }
                                         }
                                         """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateSemanticAdapter(model).Adapt(queryWithArgument));

        Assert.Contains("argument", ex.Message);
    }

    [Fact]
    public void Mutation_is_not_silently_translated_as_a_query()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        const string mutation = """
                                mutation {
                                  customer { id }
                                }
                                """;

        var ex = Assert.Throws<InvalidOperationException>(() => new HotChocolateSemanticAdapter(model).Adapt(mutation));

        Assert.Contains("query operations only", ex.Message);
    }
}