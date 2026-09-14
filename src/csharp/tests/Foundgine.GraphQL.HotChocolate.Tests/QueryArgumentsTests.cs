using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class QueryArgumentsTests
{
    [Fact]
    public void Where_order_and_paging_translate_into_semantic_options()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query {
                                                                     customer(where: { name: { eq: "Alice" } }, order: { name: DESC }, first: 10, skip: 2) {
                                                                       id
                                                                       name
                                                                     }
                                                                   }
                                                                   """);

        Assert.NotNull(request.Options);
        Assert.Equal(10, request.Options!.Limit);
        Assert.Equal(2, request.Options.Offset);
        Assert.Equal(SemanticSortDirection.Desc, Assert.Single(request.Options.EffectiveOrder).Direction);
        Assert.IsType<SemanticFieldFilter>(request.Options.Filter);
    }

    [Fact]
    public void After_cursor_translates_into_semantic_options()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();

        var request = new HotChocolateSemanticAdapter(model).Adapt("""
                                                                   query {
                                                                     customer(first: 10, after: "MQ==") { id }
                                                                   }
                                                                   """);

        Assert.Equal(10, request.Options!.Limit);
        Assert.Equal("MQ==", request.Options.After);
    }
}