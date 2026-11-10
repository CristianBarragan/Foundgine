using Foundgine.Generated.Orm;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Orm;
using Xunit;
using static Foundgine.Orm.Tests.OrmTestModel;

namespace Foundgine.Orm.Tests;

/// <summary>
/// A provider's feature profile is deliberately narrower than the semantic query language.
/// These tests pin down which shapes are currently executable by the shared LINQ compiler.
/// </summary>
public sealed class OrmFeatureConformanceMatrixTests
{
    private static OrmRegistry Registry() => OrmRegistry.Create(AllMetadata(), GeneratedOrmMaps.CreateAll(), Relationships());

    [Theory]
    [InlineData("efcore")]
    public void Baseline_profile_supports_scan_filter_projection_ordering_and_limit(string provider)
    {
        var compiler = new OrmPlanCompiler(provider, Registry(), OrmProviderCapabilities.SharedLinq(provider));
        var plan = compiler.Compile(Ir(new SemanticQueryOptions(
            new SemanticFieldFilter(Active, SemanticFilterOperator.Eq, true),
            [new SemanticOrderTerm(Name, SemanticSortDirection.Asc)], Limit: 2, After: null)));
        Assert.Equal(provider, plan.Provider);
    }

    [Theory]
    [InlineData("efcore")]
    public void Baseline_profile_supports_relationship_traversal_but_not_per_parent_paging(string provider)
    {
        var compiler = new OrmPlanCompiler(provider, Registry(), OrmProviderCapabilities.SharedLinq(provider));
        var traversal = Ir(fields: [Id, Name], children: [Child(2, Order, CustomerOrders, [OrderId])]);
        Assert.NotNull(compiler.Compile(traversal));

        var childPage = Ir(fields: [Id], children: [Child(2, Order, CustomerOrders, [OrderId],
            options: new SemanticQueryOptions(Limit: 1))]);
        Assert.Throws<NotSupportedException>(() => compiler.Compile(childPage));
    }

    [Theory]
    [InlineData("efcore")]
    public void Baseline_profile_rejects_relationship_and_aggregate_filters(string provider)
    {
        var compiler = new OrmPlanCompiler(provider, Registry(), OrmProviderCapabilities.SharedLinq(provider));
        var relationshipFilter = new SemanticRelationshipFilter(CustomerOrders,
            SemanticRelationshipQuantifier.Some,
            new SemanticFieldFilter(Sku, SemanticFilterOperator.Eq, "A"));
        var aggregateFilter = new SemanticAggregateFilter(CustomerOrders,
            SemanticFilterAggregate.Count, null, SemanticAggregateFilterOperator.Gt, 0);

        Assert.Throws<NotSupportedException>(() => compiler.Compile(Ir(new SemanticQueryOptions(relationshipFilter))));
        Assert.Throws<NotSupportedException>(() => compiler.Compile(Ir(new SemanticQueryOptions(aggregateFilter))));
    }

    [Theory]
    [InlineData("efcore")]
    public void Baseline_profile_rejects_every_aggregate_ordering_kind(string provider)
    {
        var compiler = new OrmPlanCompiler(provider, Registry(), OrmProviderCapabilities.SharedLinq(provider));
        foreach (var aggregate in new[]
                 { SemanticOrderAggregate.Count, SemanticOrderAggregate.Min, SemanticOrderAggregate.Max })
        {
            var options = new SemanticQueryOptions(Order:
                [new SemanticOrderTerm(Total, SemanticSortDirection.Desc, [CustomerOrders], aggregate)]);
            Assert.Contains("does not support required execution features",
                Assert.Throws<NotSupportedException>(() => compiler.Compile(Ir(options))).Message);
        }
    }

    [Theory]
    [InlineData(SemanticOrderAggregate.Count)]
    [InlineData(SemanticOrderAggregate.Min)]
    [InlineData(SemanticOrderAggregate.Max)]
    public void Aggregate_ordering_remains_rejected_when_combined_with_paging(SemanticOrderAggregate aggregate)
    {
        var provider = "efcore";
        var compiler = new OrmPlanCompiler(provider, Registry(), OrmProviderCapabilities.SharedLinq(provider));
        var options = new SemanticQueryOptions(
            Order: [new SemanticOrderTerm(Total, SemanticSortDirection.Asc, [CustomerOrders], aggregate)],
            Limit: 10,
            After: null);

        Assert.Throws<NotSupportedException>(() => compiler.Compile(Ir(options)));
    }


    [Theory]
    [InlineData("efcore")]
    public void Declaring_advanced_flags_does_not_bypass_authorization_and_translation_gates(string provider)
    {
        var baseline = OrmProviderCapabilities.SharedLinq(provider);
        var advanced = baseline with
        {
            Features = baseline.Features | OrmProviderFeature.RelationshipFilters |
                       OrmProviderFeature.AggregateFilters | OrmProviderFeature.AggregateOrdering |
                       OrmProviderFeature.PerParentPagination
        };
        var compiler = new OrmPlanCompiler(provider, Registry(), advanced);

        var relationshipFilter = new SemanticRelationshipFilter(CustomerOrders,
            SemanticRelationshipQuantifier.Some,
            new SemanticFieldFilter(Sku, SemanticFilterOperator.Eq, "A"));
        var aggregateFilter = new SemanticAggregateFilter(CustomerOrders,
            SemanticFilterAggregate.Count, null, SemanticAggregateFilterOperator.Gt, 0);
        var childAuthorization = Foundgine.Core.Abstractions.AuthorizationPredicate.Equal(
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ContextParameter("ctx"), "CustomerId"));
        var authorizedChild = Child(2, Order, CustomerOrders, [OrderId], authorization: childAuthorization);
        var countOrder = new SemanticQueryOptions(Order:
            [new SemanticOrderTerm(OrderId, SemanticSortDirection.Desc, [CustomerOrders], SemanticOrderAggregate.Count)]);
        var minMaxOrders = new[]
        {
            new SemanticQueryOptions(Order: [new SemanticOrderTerm(Total, SemanticSortDirection.Desc, [CustomerOrders], SemanticOrderAggregate.Min)]),
            new SemanticQueryOptions(Order: [new SemanticOrderTerm(Total, SemanticSortDirection.Desc, [CustomerOrders], SemanticOrderAggregate.Max)])
        };
        var aggregateOrder = new[] { new SemanticOrderTerm(OrderId, SemanticSortDirection.Desc,
            [CustomerOrders], SemanticOrderAggregate.Count) };
        var pagedAggregateOrders = new[]
        {
            new SemanticQueryOptions(Order: aggregateOrder, Limit: 10),
            new SemanticQueryOptions(Order: aggregateOrder, Offset: 10),
            new SemanticQueryOptions(Order: aggregateOrder, After: OrmCursor.Encode([2]))
        };
        var childPage = Ir(fields: [Id], children: [Child(2, Order, CustomerOrders, [OrderId],
            options: new SemanticQueryOptions(Limit: 1))]);

        Assert.Contains("requires matching child execution nodes", Assert.Throws<NotSupportedException>(
            () => compiler.Compile(Ir(new SemanticQueryOptions(relationshipFilter)))).Message);
        Assert.Contains("requires one consistent authorized child execution node", Assert.Throws<NotSupportedException>(
            () => compiler.Compile(Ir(new SemanticQueryOptions(aggregateFilter)))).Message);
        Assert.NotNull(compiler.Compile(Ir(countOrder, children: [authorizedChild])));
        Assert.Contains("requires a matching child execution node with authorization", Assert.Throws<InvalidOperationException>(
            () => compiler.Compile(Ir(countOrder))).Message);
        foreach (var aggregateOrder in minMaxOrders)
            Assert.NotNull(compiler.Compile(Ir(aggregateOrder, children: [authorizedChild])));
        foreach (var aggregateOrder in minMaxOrders)
            Assert.Throws<InvalidOperationException>(() => compiler.Compile(Ir(aggregateOrder)));
        foreach (var pagedAggregateOrder in pagedAggregateOrders)
        {
            var error = Assert.Throws<NotSupportedException>(
                () => compiler.Compile(Ir(pagedAggregateOrder, children: [authorizedChild])));
            Assert.Contains("cannot yet be combined with", error.Message);
        }
        Assert.Contains("no generated translation strategy", Assert.Throws<NotSupportedException>(
            () => compiler.Compile(childPage)).Message);
    }

    [Fact]
    public void Advanced_features_are_known_but_not_claimed_by_the_shared_linq_profile()
    {
        var profile = OrmProviderCapabilities.SharedLinq("efcore");
        Assert.Equal(OrmProviderFeature.AllRead, profile.Features);
        Assert.Equal(OrmProviderFeature.None,
            profile.Features & (OrmProviderFeature.RelationshipFilters | OrmProviderFeature.AggregateFilters |
                OrmProviderFeature.AggregateOrdering | OrmProviderFeature.PerParentPagination));

        var explicitAdvancedProfile = profile with
        {
            Features = profile.Features | OrmProviderFeature.RelationshipFilters | OrmProviderFeature.AggregateFilters |
                       OrmProviderFeature.AggregateOrdering | OrmProviderFeature.PerParentPagination
        };
        Assert.Same(explicitAdvancedProfile, explicitAdvancedProfile.ValidateFor("efcore"));
    }
}
