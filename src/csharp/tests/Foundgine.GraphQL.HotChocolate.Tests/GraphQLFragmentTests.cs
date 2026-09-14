using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class GraphQLFragmentTests
{
    [Fact]
    public void Query_named_fragment_is_expanded_into_the_semantic_selection_set()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query {
                                                                     customer {
                                                                       ...CustomerFields
                                                                       accounts { ...AccountFields }
                                                                     }
                                                                   }

                                                                   fragment CustomerFields on Customer {
                                                                     id
                                                                     name
                                                                   }

                                                                   fragment AccountFields on Account {
                                                                     id
                                                                     name
                                                                   }
                                                                   """);

        Assert.Equal(new[] { new FieldId(1), new FieldId(2) },
            request.Selections.Where(x => x.Field is not null).Select(x => x.Field!.Value));
        var accounts = Assert.Single(request.Selections, x => x.Relationship is not null);
        Assert.Equal(new[] { new FieldId(1), new FieldId(2) },
            accounts.Children.Select(x => x.Field!.Value));
    }

    [Fact]
    public void Mutation_named_fragment_is_expanded_into_return_fields()
    {
        var (model, metadata) = BuildCustomer();
        var intent = new HotChocolateMutationAdapter(model, metadata).Adapt("""
                                                                            mutation CreateCustomer($input: CustomerInput!) {
                                                                              createCustomer(input: $input) {
                                                                                ...CustomerFields
                                                                              }
                                                                            }

                                                                            fragment CustomerFields on Customer {
                                                                              id
                                                                              name
                                                                            }
                                                                            """, new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["name"] = "Ada" }
        });

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal(new[] { new FieldId(1), new FieldId(2) }, mutation.ReturnFields);
    }

    [Fact]
    public void Nested_named_fragments_can_be_composed()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query {
                                                                     customer { ...Outer }
                                                                   }

                                                                   fragment Outer on Customer {
                                                                     ...Inner
                                                                     name
                                                                   }

                                                                   fragment Inner on Customer {
                                                                     id
                                                                   }
                                                                   """);

        Assert.Equal(new[] { new FieldId(1), new FieldId(2) },
            request.Selections.Select(x => x.Field!.Value));
    }

    [Fact]
    public void Fragment_cycle_is_rejected()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateSemanticAdapter(model).Adapt("""
                                                         query { customer { ...A } }
                                                         fragment A on Customer { ...B }
                                                         fragment B on Customer { ...A }
                                                         """));

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fragment_with_wrong_type_condition_is_rejected()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Entity(account, "Account", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateSemanticAdapter(model).Adapt("""
                                                         query { customer { ...AccountFields } }
                                                         fragment AccountFields on Account { id }
                                                         """));

        Assert.Contains("targets", ex.Message);
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
            .Entity(customer, "Customer",
                e => e.Identity(new FieldId(1), "Id").Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        return (model, registry);
    }
}