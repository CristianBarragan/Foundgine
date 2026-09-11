using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class GraphQLAliasTests
{
    [Fact]
    public void Query_alias_is_preserved_in_adapter_result_projection()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var adaptation = new HotChocolateSemanticAdapter(model).AdaptResultShape("""
            query {
              customer {
                customerId: id
                displayName: name
              }
            }
            """);

        Assert.Equal(new[] { new FieldId(1), new FieldId(2) },
            adaptation.Request.Selections.Select(x => x.Field!.Value));

        Assert.Equal("customerId", adaptation.Result.Fields[0].Alias);
        Assert.Equal("id", adaptation.Result.Fields[0].GraphQLName);
        Assert.Equal("displayName", adaptation.Result.Fields[1].Alias);
        Assert.Equal("name", adaptation.Result.Fields[1].GraphQLName);
    }

    [Fact]
    public void Relationship_alias_is_preserved_with_child_projection()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var adaptation = new HotChocolateSemanticAdapter(model).AdaptResultShape("""
            query {
              customer {
                accountsList: accounts {
                  accountName: name
                }
              }
            }
            """);

        var relationship = Assert.Single(adaptation.Result.Fields);
        Assert.Equal("accountsList", relationship.Alias);
        Assert.Equal("accounts", relationship.GraphQLName);
        var child = Assert.Single(relationship.Children!.Fields);
        Assert.Equal("accountName", child.Alias);
        Assert.Equal(new FieldId(2), child.Field);
    }

    [Fact]
    public void Mutation_alias_is_preserved_in_adapter_result_projection()
    {
        var (model, metadata) = BuildCustomer();

        var adaptation = new HotChocolateMutationAdapter(model, metadata).AdaptResultShape("""
            mutation {
              createCustomer(input: { name: "Ada" }) {
                customerId: id
                displayName: name
              }
            }
            """);

        var mutation = Assert.IsType<MutationIntent>(adaptation.Intent.Mutation);
        Assert.Equal(new[] { new FieldId(1), new FieldId(2) }, mutation.ReturnFields);

        Assert.Equal("customerId", adaptation.Result.Fields[0].Alias);
        Assert.Equal("displayName", adaptation.Result.Fields[1].Alias);
    }

    [Fact]
    public void Alias_does_not_change_provider_neutral_semantic_request()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var adapter = new HotChocolateSemanticAdapter(model);
        var literal = adapter.Adapt("query { customer { id name } }");
        var aliased = adapter.Adapt("query { customer { x: id y: name } }");

        Assert.Equal(literal.Root, aliased.Root);
        Assert.Equal(
            literal.Selections.Select(x => (x.Field, x.Relationship, x.Children.Count)),
            aliased.Selections.Select(x => (x.Field, x.Relationship, x.Children.Count)));
        Assert.Equal(literal.Options?.Limit, aliased.Options?.Limit);
        Assert.Equal(literal.Options?.Offset, aliased.Options?.Offset);
        Assert.Equal(literal.Options?.After, aliased.Options?.After);
    }

    private static (SemanticModel Model, MetadataRegistry Metadata) BuildCustomer()
    {
        var customer = new EntityId(1);
        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(customer, "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(customer, new ColumnId(1))));

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        return (model, registry);
    }
}