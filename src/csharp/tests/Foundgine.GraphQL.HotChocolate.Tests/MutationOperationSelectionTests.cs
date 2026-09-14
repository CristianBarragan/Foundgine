using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class MutationOperationSelectionTests
{
    [Fact]
    public void Named_operation_is_selected_from_multi_operation_document()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer($input: CustomerInput!) {
                                     createCustomer(input: $input) { id name }
                                   }

                                   mutation UpdateCustomer($input: CustomerInput!, $where: CustomerWhereInput!) {
                                     updateCustomer(input: $input, where: $where) { id name }
                                   }
                                   """, "UpdateCustomer", new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["name"] = "Ada" },
            ["where"] = new Dictionary<string, object?> { ["id"] = 7 }
        });

        Assert.Equal(MutationKind.Update, intent.Mutation.Kind);
    }

    [Fact]
    public void Multiple_operations_require_an_operation_name()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                                                                              mutation DeleteCustomer { deleteCustomer(where: { id: { eq: 7 } }) { id } }
                                                                              """));

        Assert.Contains("operation name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_operation_name_is_rejected()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                                                                              mutation UpdateCustomer { updateCustomer(input: { name: "Grace" }, where: { id: { eq: 7 } }) { id } }
                                                                              """, "DeleteCustomer"));

        Assert.Contains("DeleteCustomer", ex.Message);
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selected_query_operation_is_rejected()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              query CustomerQuery { customer { id name } }
                                                                              mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                                                                              """, "CustomerQuery"));

        Assert.Contains("mutation operation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Single_anonymous_mutation_still_works_without_operation_name()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation { createCustomer(input: { name: "Ada" }) { id name } }
                                   """);

        Assert.Equal(MutationKind.Create, intent.Mutation.Kind);
    }

    [Fact]
    public void Operation_name_can_be_selected_without_variables()
    {
        var (model, registry) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, registry);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                                   mutation UpdateCustomer { updateCustomer(input: { name: "Grace" }, where: { id: { eq: 7 } }) { id } }
                                   """, "CreateCustomer");

        Assert.Equal(MutationKind.Create, intent.Mutation.Kind);
        Assert.Equal("Ada", Assert.Single(Assert.IsType<MutationIntent>(intent.Mutation).Fields).Value);
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