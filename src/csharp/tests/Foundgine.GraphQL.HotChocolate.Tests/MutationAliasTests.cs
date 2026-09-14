using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class MutationAliasTests
{
    [Fact]
    public void Alias_is_kept_out_of_mutation_intent_and_preserved_in_result_shape()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var adapted = adapter.AdaptWithResultShape("""
                                                   mutation CreateCustomer {
                                                     createCustomer(input: { name: "Ada" }) {
                                                       customerId: id
                                                       displayName: name
                                                     }
                                                   }
                                                   """);

        var mutation = Assert.IsType<Foundgine.Core.Semantic.Planning.Mutation.MutationIntent>(adapted.Intent.Mutation);
        Assert.Equal([new FieldId(1), new FieldId(2)], mutation.ReturnFields);
        Assert.Equal(
            [
                new GraphQLMutationResultField(new FieldId(1), "customerId"),
                new GraphQLMutationResultField(new FieldId(2), "displayName")
            ],
            adapted.ResultShape.Fields);
    }

    [Fact]
    public void Alias_can_be_applied_to_materialized_result()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);
        var adapted = adapter.AdaptWithResultShape("""
                                                   mutation CreateCustomer {
                                                     createCustomer(input: { name: "Ada" }) {
                                                       customerId: id
                                                       displayName: name
                                                     }
                                                   }
                                                   """);

        var materialized = new MutationMaterializedNode(
            0,
            new EntityId(1),
            new Dictionary<FieldId, object?>
            {
                [new FieldId(1)] = 42L,
                [new FieldId(2)] = "Ada"
            });

        var shaped = GraphQLMutationResultShaper.Shape(materialized, adapted.ResultShape);

        Assert.Equal(42L, shaped["customerId"]);
        Assert.Equal("Ada", shaped["displayName"]);
    }

    [Fact]
    public void Same_response_alias_is_rejected()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.AdaptWithResultShape("""
            mutation CreateCustomer {
              createCustomer(input: { name: "Ada" }) {
                value: id
                value: name
              }
            }
            """));

        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
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