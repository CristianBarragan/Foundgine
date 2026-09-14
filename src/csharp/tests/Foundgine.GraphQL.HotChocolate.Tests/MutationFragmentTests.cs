using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class MutationFragmentTests
{
    [Fact]
    public void Named_fragment_is_expanded_into_mutation_result_fields()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer {
                                     createCustomer(input: { name: "Ada" }) {
                                       ...CustomerFields
                                     }
                                   }

                                   fragment CustomerFields on Customer {
                                     id
                                     name
                                   }
                                   """);

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal([new FieldId(1), new FieldId(2)], mutation.ReturnFields);
    }

    [Fact]
    public void Fragment_fields_are_deduplicated()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer {
                                     createCustomer(input: { name: "Ada" }) {
                                       id
                                       ...CustomerFields
                                     }
                                   }

                                   fragment CustomerFields on Customer {
                                     id
                                     name
                                   }
                                   """);

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal([new FieldId(1), new FieldId(2)], mutation.ReturnFields);
    }

    [Fact]
    public void Fragment_cycle_is_rejected()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer {
                                                                                createCustomer(input: { name: "Ada" }) { ...A }
                                                                              }
                                                                              fragment A on Customer { ...B }
                                                                              fragment B on Customer { ...A }
                                                                              """));

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_fragment_is_rejected()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer {
                                                                                createCustomer(input: { name: "Ada" }) { ...MissingFields }
                                                                              }
                                                                              """));

        Assert.Contains("MissingFields", ex.Message);
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fragment_on_different_type_is_rejected()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer {
                                                                                createCustomer(input: { name: "Ada" }) { ...AccountFields }
                                                                              }
                                                                              fragment AccountFields on Account { id }
                                                                              """));

        Assert.Contains("cannot be applied", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inline_fragment_on_matching_type_is_supported()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer {
                                     createCustomer(input: { name: "Ada" }) {
                                       ... on Customer { id name }
                                     }
                                   }
                                   """);

        var mutation = Assert.IsType<MutationIntent>(intent.Mutation);
        Assert.Equal([new FieldId(1), new FieldId(2)], mutation.ReturnFields);
    }

    private static (SemanticModel Model, MetadataRegistry Registry) BuildCustomer()
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