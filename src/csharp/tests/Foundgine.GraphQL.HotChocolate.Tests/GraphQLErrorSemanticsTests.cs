using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class GraphQLErrorSemanticsTests
{
    [Fact]
    public void QueryTryAdapt_ReturnsStructuredValidationError()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        var result = new HotChocolateSemanticAdapter(model).TryAdapt(
            "query { customer { missing } }");

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("GRAPHQL_VALIDATION_FAILED", error.Code);
        Assert.Contains("missing", error.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void MutationTryAdapt_ReturnsBadUserInputForVariableFailure()
    {
        var (model, metadata) = BuildCustomer();

        var result = new HotChocolateMutationAdapter(model, metadata).TryAdapt("""
                                                                               mutation CreateCustomer($name: String!) {
                                                                                 createCustomer(input: { name: $name }) { id }
                                                                               }
                                                                               """,
            new Dictionary<string, object?> { ["name"] = 42 });

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("BAD_USER_INPUT", error.Code);
        Assert.Contains("String", error.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void SuccessfulTryAdapt_ReturnsDataAndNoErrors()
    {
        var (model, metadata) = BuildCustomer();

        var result = new HotChocolateMutationAdapter(model, metadata).TryAdapt("""
                                                                               mutation CreateCustomer {
                                                                                 createCustomer(input: { name: "Ada" }) { id name }
                                                                               }
                                                                               """);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ExistingAdaptApi_StillThrows()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new HotChocolateSemanticAdapter(model).Adapt("query { customer { missing } }"));
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
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        return (model, registry);
    }
}