package com.foundgine.providers.storage.sql;

import com.foundgine.core.semantic.metadata.IMetadataProvider;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.query.*;
import java.time.Instant;

/** SQL provider cost estimator used by the provider-aware rewrite selector. */
public final class SqlCostEstimator implements IProviderCostEstimator {
	private final IMetadataProvider metadata;
	private final SqlCostModelOptions options;

	public SqlCostEstimator(IMetadataProvider metadata, SqlCostModelOptions options) {
		this.metadata = java.util.Objects.requireNonNull(metadata);
		this.options = (options == null ? new SqlCostModelOptions() : options).validate();
	}

	public SqlCostEstimator(IMetadataProvider metadata) {
		this(metadata, null);
	}

	@Override
	public String provider() {
		return "sql";
	}

	@Override
	public ProviderCostEstimate estimate(SemanticPlan before, SemanticPlan candidate, IPlanRewriteRule rule) {
		java.util.Objects.requireNonNull(before);
		java.util.Objects.requireNonNull(candidate);
		java.util.Objects.requireNonNull(rule);
		double cost = estimateNode(candidate.root());
		double rows = estimateRows(candidate.root());
		CostEstimateProvenance provenance = options.statisticsObservedAtUtc() != null
				&& options.statisticsVersion() != null
						? CostEstimateProvenance.fromStatistics(options.statisticsSource(), options.statisticsVersion(),
								options.statisticsObservedAtUtc(), Instant.now(), options.statisticsStaleAfter())
						: CostEstimateProvenance.heuristic(options.statisticsSource(), Instant.now());
		double confidence = switch (provenance.freshness()) {
		case FRESH -> .9d;
		case STALE -> .3d;
		default -> .5d;
		};
		return ProviderCostEstimate.from(provider(), Math.max(0d, cost), rows, confidence, provenance);
	}

	private double estimateNode(SemanticPlanNode node) {
		var entity = metadata.getEntity(node.entityId());
		double cost = options.scanBaseCost() + node.fields().size() * options.fieldCost();
		if (node.operation() == ExecutionOperation.TRAVERSE
				|| node.operation() == ExecutionOperation.TRAVERSE_CONNECTION) {
			double tc = options.traverseCost();
			if (node.traversalOrder() >= 0) {
				double f = 1d - (options.traversalOrderDiscount() / (node.traversalOrder() + 2d));
				tc *= Math.max(0d, f);
			}
			cost += tc;
		}
		if (node.queryOptions() != null) {
			var q = node.queryOptions();
			if (q.filter() != null)
				cost += estimateFilter(q.filter());
			cost += q.effectiveOrder().size() * options.orderTermCost();
			if (q.limit() != null && q.limit() > 0)
				cost = Math.max(1d, cost - options.limitAdjustment());
			if (q.offset() != null && q.offset() > 0)
				cost += options.offsetCost();
			if (q.hasCursor())
				cost = Math.max(0d, cost + options.cursorAdjustment());
		}
		entity.name();
		for (var child : node.children())
			cost += estimateNode(child);
		return cost;
	}

	private double estimateFilter(SemanticFilterExpression e) {
		if (e instanceof SemanticFieldFilter)
			return options.filterCost();
		if (e instanceof SemanticRelationshipFilter r)
			return options.relationshipFilterCost() + estimateFilter(r.predicate());
		if (e instanceof SemanticAggregateFilter a)
			return options.aggregateFilterCost() + (a.predicate() == null ? 0d : estimateFilter(a.predicate()));
		if (e instanceof SemanticAndFilter a)
			return a.expressions().stream().mapToDouble(this::estimateFilter).sum();
		if (e instanceof SemanticOrFilter o)
			return o.expressions().stream().mapToDouble(this::estimateFilter).sum();
		return options.filterCost();
	}

	private static double estimateRows(SemanticPlanNode n) {
		double local = n.queryOptions() != null && n.queryOptions().limit() != null && n.queryOptions().limit() > 0
				? n.queryOptions().limit()
				: 1000d;
		return Math.max(1d, local + n.children().stream().mapToDouble(SqlCostEstimator::estimateRows).sum());
	}
}
