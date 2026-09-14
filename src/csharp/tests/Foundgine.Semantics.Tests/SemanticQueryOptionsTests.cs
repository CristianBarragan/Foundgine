using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticQueryOptionsTests
{
    [Fact]
    public void Filter_and_order_are_protocol_neutral()
    {
        var filter = new SemanticAndFilter([
            new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.Eq, "Alice"),
            new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.In, new object?[] { 1, 2 })
        ]);

        var options = new SemanticQueryOptions(
            filter,
            [new SemanticOrderTerm(new FieldId(2), SemanticSortDirection.Desc)],
            Limit: 10,
            Offset: 20);

        Assert.IsType<SemanticAndFilter>(options.Filter);
        Assert.Equal(SemanticSortDirection.Desc, options.EffectiveOrder[0].Direction);
        Assert.Equal(10, options.Limit);
        Assert.Equal(20, options.Offset);
    }
}
