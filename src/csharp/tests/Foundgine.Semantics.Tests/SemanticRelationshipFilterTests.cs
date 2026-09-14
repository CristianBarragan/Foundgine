using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticRelationshipFilterTests
{
    [Fact]
    public void Relationship_filter_is_protocol_neutral()
    {
        var filter = new SemanticRelationshipFilter(
            new RelationshipId(1),
            SemanticRelationshipQuantifier.Some,
            new SemanticFieldFilter(new FieldId(3), SemanticFilterOperator.Eq, 100m));

        Assert.Equal(SemanticRelationshipQuantifier.Some, filter.Quantifier);
        Assert.Equal(new RelationshipId(1), filter.Relationship);
    }
}
