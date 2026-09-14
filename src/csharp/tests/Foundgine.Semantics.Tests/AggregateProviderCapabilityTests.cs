using Foundgine.Core.Semantic.Aggregates;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class AggregateProviderCapabilityTests
{
    [Theory]
    [InlineData(SemanticFilterAggregate.Count)]
    [InlineData(SemanticFilterAggregate.Min)]
    [InlineData(SemanticFilterAggregate.Max)]
    public void GenericSql_supports_every_catalogued_aggregate(SemanticFilterAggregate aggregate)
    {
        Assert.True(AggregateProviderCapabilityRegistry.GenericSql.Supports(aggregate));
    }

    [Fact]
    public void GenericSql_declares_provider_name_and_predicate_and_quantifier_support()
    {
        var sql = AggregateProviderCapabilityRegistry.GenericSql;

        Assert.Equal("sql", sql.ProviderName);
        Assert.True(sql.SupportsAggregatePredicate);
        Assert.True(sql.SupportsRelationshipQuantifiers);
    }

    [Fact]
    public void A_provider_with_a_narrower_declared_set_does_not_support_undeclared_aggregates()
    {
        var narrow = new AggregateProviderCapability(
            "graphql-experimental",
            [SemanticFilterAggregate.Count],
            SupportsAggregatePredicate: false,
            SupportsRelationshipQuantifiers: false);

        Assert.True(narrow.Supports(SemanticFilterAggregate.Count));
        Assert.False(narrow.Supports(SemanticFilterAggregate.Min));
        Assert.False(narrow.Supports(SemanticFilterAggregate.Max));
    }

    [Fact]
    public void A_provider_with_no_declared_aggregates_supports_nothing()
    {
        var empty = new AggregateProviderCapability(
            "static-cache",
            [],
            SupportsAggregatePredicate: false,
            SupportsRelationshipQuantifiers: false);

        Assert.False(empty.Supports(SemanticFilterAggregate.Count));
        Assert.False(empty.Supports(SemanticFilterAggregate.Min));
        Assert.False(empty.Supports(SemanticFilterAggregate.Max));
    }
}
