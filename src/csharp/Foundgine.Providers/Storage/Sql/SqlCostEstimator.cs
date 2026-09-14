using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Providers.Storage.Sql;

/// <summary>
/// Conservative provider-aware cost model for SQL plan selection.
/// It is intentionally heuristic and provider-neutral at the planning API;
/// deployments may replace it with statistics-backed estimates later.
/// </summary>
public sealed record SqlCostModelOptions(
    double ScanBaseCost = 10d,
    double FieldCost = 0.25d,
    double TraverseCost = 3d,
    double FilterCost = 1.5d,
    double RelationshipFilterCost = 4d,
    double AggregateFilterCost = 5d,
    double OrderTermCost = 1.25d,
    double LimitAdjustment = 0.5d,
    double OffsetCost = 2d,
    double CursorAdjustment = -1d,
    double TraversalOrderDiscount = 0.75d,
    string StatisticsSource = "heuristic",
    string? StatisticsVersion = null,
    DateTimeOffset? StatisticsObservedAtUtc = null,
    TimeSpan? StatisticsStaleAfter = null)
{
    public SqlCostModelOptions Validate()
    {
        ValidateNonNegative(ScanBaseCost, nameof(ScanBaseCost));
        ValidateNonNegative(FieldCost, nameof(FieldCost));
        ValidateNonNegative(TraverseCost, nameof(TraverseCost));
        ValidateNonNegative(FilterCost, nameof(FilterCost));
        ValidateNonNegative(RelationshipFilterCost, nameof(RelationshipFilterCost));
        ValidateNonNegative(AggregateFilterCost, nameof(AggregateFilterCost));
        ValidateNonNegative(OrderTermCost, nameof(OrderTermCost));
        ValidateNonNegative(LimitAdjustment, nameof(LimitAdjustment));
        ValidateNonNegative(OffsetCost, nameof(OffsetCost));
        if (double.IsNaN(CursorAdjustment) || double.IsInfinity(CursorAdjustment))
            throw new ArgumentOutOfRangeException(nameof(CursorAdjustment));
        ValidateNonNegative(TraversalOrderDiscount, nameof(TraversalOrderDiscount));
        if (string.IsNullOrWhiteSpace(StatisticsSource))
            throw new ArgumentException("Statistics source is required.", nameof(StatisticsSource));
        if (StatisticsObservedAtUtc is not null && StatisticsVersion is null)
            throw new ArgumentException("Statistics version is required when an observation timestamp is supplied.",
                nameof(StatisticsVersion));
        if (StatisticsStaleAfter is not null && StatisticsStaleAfter.Value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StatisticsStaleAfter));
        return this;
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>
/// SQL provider cost estimator used by the provider-aware rewrite selector.
/// It uses semantic plan shape and metadata, never SQL text or request authority.
/// </summary>
public sealed class SqlCostEstimator : IProviderCostEstimator
{
    private readonly IMetadataProvider _metadata;
    private readonly SqlCostModelOptions _options;

    public SqlCostEstimator(IMetadataProvider metadata, SqlCostModelOptions? options = null)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _options = (options ?? new SqlCostModelOptions()).Validate();
    }

    public string Provider => "sql";

    public ProviderCostEstimate Estimate(
        SemanticPlan before,
        SemanticPlan candidate,
        IPlanRewriteRule rule)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(rule);

        var cost = EstimateNode(candidate.Root);
        var rows = EstimateRows(candidate.Root);
        var provenance = _options.StatisticsObservedAtUtc is { } observedAt && _options.StatisticsVersion is { } version
            ? CostEstimateProvenance.FromStatistics(
                _options.StatisticsSource,
                version,
                observedAt,
                staleAfter: _options.StatisticsStaleAfter)
            : CostEstimateProvenance.Heuristic(_options.StatisticsSource);

        var confidence = provenance.Freshness switch
        {
            CostStatisticsFreshness.Fresh => 0.9d,
            CostStatisticsFreshness.Stale => 0.3d,
            _ => 0.5d
        };
        return ProviderCostEstimate.From(Provider, Math.Max(0d, cost), rows, confidence, provenance);
    }

    private double EstimateNode(SemanticPlanNode node)
    {
        var entity = _metadata.GetEntity(node.EntityId);
        var cost = _options.ScanBaseCost + (node.Fields.Count * _options.FieldCost);

        if (node.Operation is ExecutionOperation.Traverse or ExecutionOperation.TraverseConnection)
        {
            var traversalCost = _options.TraverseCost;
            if (node.TraversalOrder >= 0)
            {
                var orderFactor = 1d - (_options.TraversalOrderDiscount / (node.TraversalOrder + 2d));
                traversalCost *= Math.Max(0d, orderFactor);
            }

            cost += traversalCost;
        }

        if (node.QueryOptions is { } query)
        {
            if (query.Filter is not null)
                cost += EstimateFilter(query.Filter);

            cost += query.EffectiveOrder.Count * _options.OrderTermCost;

            if (query.Limit is > 0)
                cost = Math.Max(1d, cost - _options.LimitAdjustment);

            if (query.Offset is > 0)
                cost += _options.OffsetCost;

            if (query.HasCursor)
                cost = Math.Max(0d, cost + _options.CursorAdjustment);
        }

        // Metadata lookup is intentionally part of the estimate so future
        // statistics-backed implementations have a stable provider boundary.
        _ = entity.Name;

        foreach (var child in node.Children)
            cost += EstimateNode(child);

        return cost;
    }

    private double EstimateFilter(SemanticFilterExpression expression) => expression switch
    {
        SemanticFieldFilter => _options.FilterCost,
        SemanticRelationshipFilter relationship =>
            _options.RelationshipFilterCost + EstimateFilter(relationship.Predicate),
        SemanticAggregateFilter aggregate =>
            _options.AggregateFilterCost + (aggregate.Predicate is null ? 0d : EstimateFilter(aggregate.Predicate)),
        SemanticAndFilter and => and.Expressions.Sum(EstimateFilter),
        SemanticOrFilter or => or.Expressions.Sum(EstimateFilter),
        _ => _options.FilterCost
    };

    private static double EstimateRows(SemanticPlanNode node)
    {
        var local = node.QueryOptions?.Limit is > 0
            ? node.QueryOptions.Limit.Value
            : 1000d;

        return Math.Max(1d, local + node.Children.Sum(EstimateRows));
    }
}