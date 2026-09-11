using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class GraphQLDirectiveTests
{
    [Fact]
    public void IncludeLiteralFalse_RemovesField()
    {
        var model = BuildModel();
        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query { customer { id name @include(if: false) } }
                                                                   """);

        Assert.Single(request.Selections);
        Assert.Equal(new FieldId(1), request.Selections[0].Field);
    }

    [Fact]
    public void SkipLiteralTrue_RemovesField()
    {
        var model = BuildModel();
        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query { customer { id name @skip(if: true) } }
                                                                   """);

        Assert.Single(request.Selections);
        Assert.Equal(new FieldId(1), request.Selections[0].Field);
    }

    [Fact]
    public void IncludeVariableTrue_AddsField()
    {
        var model = BuildModel();
        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query Customer($withName: Boolean!) {
                                                                     customer { id name @include(if: $withName) }
                                                                   }
                                                                   """,
            new Dictionary<string, object?> { ["withName"] = true });

        Assert.Equal(2, request.Selections.Count);
    }

    [Fact]
    public void SkipVariableTrue_RemovesField()
    {
        var model = BuildModel();
        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query Customer($hideName: Boolean!) {
                                                                     customer { id name @skip(if: $hideName) }
                                                                   }
                                                                   """,
            new Dictionary<string, object?> { ["hideName"] = true });

        Assert.Single(request.Selections);
    }


    [Fact]
    public void IncludeVariableDefault_IsUsed()
    {
        var model = BuildModel();
        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query Customer($withName: Boolean! = true) {
                                                                     customer { id name @include(if: $withName) }
                                                                   }
                                                                   """);

        Assert.Equal(2, request.Selections.Count);
    }

    [Fact]
    public void FragmentSpreadDirective_IsEvaluated()
    {
        var model = BuildModel();
        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query Customer($includeDetails: Boolean!) {
                                                                     customer { id ...Details @include(if: $includeDetails) }
                                                                   }
                                                                   fragment Details on Customer { name }
                                                                   """,
            new Dictionary<string, object?> { ["includeDetails"] = false });

        Assert.Single(request.Selections);
    }

    [Fact]
    public void MutationResultDirective_IsEvaluated()
    {
        var (model, metadata) = BuildCustomer();
        var adaptation = new HotChocolateMutationAdapter(model, metadata).AdaptResultShape("""
            mutation CreateCustomer($includeName: Boolean!) {
              createCustomer(input: { name: "Ada" }) {
                id
                name @include(if: $includeName)
              }
            }
            """, new Dictionary<string, object?> { ["includeName"] = false });

        Assert.Single(adaptation.Result.Fields);
        Assert.Equal("id", adaptation.Result.Fields[0].GraphQLName);
    }

    [Fact]
    public void UnsupportedDirective_IsRejected()
    {
        var model = BuildModel();
        var ex = Assert.Throws<InvalidOperationException>(() => new HotChocolateSemanticAdapter(model).Adapt("""
            query { customer { id @deprecated(reason: "no") } }
            """));

        Assert.Contains("@deprecated", ex.Message);
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

        return (BuildModel(), registry);
    }
}