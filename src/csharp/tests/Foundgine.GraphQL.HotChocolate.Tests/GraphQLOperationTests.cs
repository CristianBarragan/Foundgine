using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class GraphQLOperationTests
{
    [Fact]
    public void Query_MultipleOperations_RequiresOperationName()
    {
        var model = BuildModel();
        var adapter = new HotChocolateSemanticAdapter(model);

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.Adapt("""
                                                                              query CustomerQuery { customer { id } }
                                                                              query OtherQuery { customer { name } }
                                                                              """));

        Assert.Contains("multiple operations", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Query_SelectsNamedOperation()
    {
        var model = BuildModel();
        var adapter = new HotChocolateSemanticAdapter(model);

        var request = adapter.Adapt("""
                                    query CustomerQuery { customer { id } }
                                    query OtherQuery { customer { name } }
                                    """, null, "OtherQuery");

        Assert.Single(request.Selections);
        Assert.Equal(new FieldId(2), request.Selections[0].Field);
    }

    [Fact]
    public void Query_UnknownOperationName_ReturnsValidationError()
    {
        var model = BuildModel();
        var result = new HotChocolateSemanticAdapter(model).TryAdapt("""
                                                                     query CustomerQuery { customer { id } }
                                                                     """, null, "MissingQuery");

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("GRAPHQL_VALIDATION_FAILED", error.Code);
        Assert.Contains("MissingQuery", error.Message);
    }

    [Fact]
    public void Mutation_SelectsNamedOperation()
    {
        var (model, metadata) = BuildCustomer();
        var adapter = new HotChocolateMutationAdapter(model, metadata);

        var intent = adapter.Adapt("""
                                   mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                                   mutation UpdateCustomer { updateCustomer(input: { name: "Grace" }, where: { id: { eq: 1 } }) { id } }
                                   """, null, "UpdateCustomer");

        Assert.Equal(Foundgine.Core.Semantic.Planning.Mutation.MutationKind.Update, intent.Mutation.Kind);
    }

    [Fact]
    public void Mutation_TryAdapt_SelectsNamedOperation()
    {
        var (model, metadata) = BuildCustomer();
        var result = new HotChocolateMutationAdapter(model, metadata).TryAdapt("""
                                                                               mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                                                                               mutation UpdateCustomer { updateCustomer(input: { name: "Grace" }, where: { id: { eq: 1 } }) { id } }
                                                                               """, null, "UpdateCustomer");

        Assert.True(result.Succeeded);
        Assert.Equal(Foundgine.Core.Semantic.Planning.Mutation.MutationKind.Update, result.Data!.Mutation.Kind);
    }

    [Fact]
    public void SingleOperation_DoesNotRequireName()
    {
        var model = BuildModel();
        var request = new HotChocolateSemanticAdapter(model).Adapt("query CustomerQuery { customer { id } }");

        Assert.Single(request.Selections);
    }

    private static SemanticModel BuildModel() => new SemanticModelBuilder()
        .Entity(new EntityId(1), "Customer", e => e
            .Identity(new FieldId(1), "Id")
            .Field(new FieldId(2), "Name", typeof(string)))
        .Build();

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

        var model = BuildModel();
        return (model, registry);
    }
}