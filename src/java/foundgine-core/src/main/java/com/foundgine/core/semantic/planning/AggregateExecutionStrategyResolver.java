package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.query.SemanticAggregateFilter;
import com.foundgine.core.semantic.query.SemanticAggregateFilterOperator;
import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import java.util.Objects;

/**
 * Port of {@code Foundgine.Core.Semantic.Planning.AggregateExecutionStrategyResolver}.
 *
 * <p>Single source of truth for whether a bare COUNT aggregate comparison (no target field,
 * no predicate) reduces to an emptiness/existence test — i.e. whether its truth value depends
 * only on whether the related collection is empty, not on the exact count.
 *
 * <p>{@link AggregateCardinalityOptimizationRule} uses this to decide what strategy hint to
 * attach to a plan node. Provider compilers (e.g. the SQL writer) use the exact same derivation
 * to decide whether an individual COUNT aggregate filter on that node still matches the node's
 * hint closely enough to be rendered as EXISTS / NOT EXISTS instead of a scalar COUNT subquery
 * comparison. Keeping both call sites on one implementation means the hint a provider acts on
 * can never silently drift from the definition that justified it.
 */
public final class AggregateExecutionStrategyResolver {

    private AggregateExecutionStrategyResolver() {
    }

    /**
     * Returns the execution strategy this comparison reduces to, or {@code null} if the
     * comparison genuinely depends on the exact count and cannot be short-circuited.
     */
    public static AggregateExecutionStrategy resolve(SemanticAggregateFilterOperator op, Object value) {
        Long count = tryGetIntegral(value);
        if (count == null) {
            return null;
        }

        // COUNT is non-negative. Only these exact thresholds collapse to a
        // pure emptiness/non-emptiness predicate. In particular, COUNT >= 0
        // and COUNT < 0 are constants and must not be treated as existence tests.
        return switch (op) {
            case GT -> count == 0 ? AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT : null;
            case GTE -> count == 1 ? AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT : null;
            case NEQ -> count == 0 ? AggregateExecutionStrategy.COUNT_EXISTS_SHORT_CIRCUIT : null;
            case EQ -> count == 0 ? AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT : null;
            case LT -> count == 1 ? AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT : null;
            case LTE -> count == 0 ? AggregateExecutionStrategy.COUNT_EMPTY_SHORT_CIRCUIT : null;
        };
    }

    /**
     * Whether {@code filter} is eligible for existence-style rendering under {@code nodeStrategy}
     * — i.e. it is a bare COUNT comparison (no field, no nested predicate) whose own comparison
     * independently resolves to that same strategy. A node-level hint only licenses rewriting
     * the specific aggregate filters that earned it; other aggregate filters sharing the node
     * (e.g. ones with a target field) are left untouched even when the node carries a
     * non-default strategy.
     */
    public static boolean isEligibleFor(SemanticAggregateFilter filter, AggregateExecutionStrategy nodeStrategy) {
        Objects.requireNonNull(filter, "filter");

        if (nodeStrategy == AggregateExecutionStrategy.DEFAULT) {
            return false;
        }

        if (filter.aggregate() != SemanticFilterAggregate.COUNT || filter.field() != null
                || filter.predicate() != null) {
            return false;
        }

        return resolve(filter.operator(), filter.value()) == nodeStrategy;
    }

    /**
     * Returns the value as a {@code long}, or {@code null} if it cannot be interpreted as one.
     *
     * <p>The C# original also matches {@code uint}/{@code ushort}/{@code sbyte} and a
     * {@code ulong} bounded by {@code long.MaxValue}; Java has no unsigned integer types, so
     * those cases collapse into the signed {@code Byte}/{@code Short}/{@code Integer}/{@code Long}
     * cases here — callers on this port's boundary only ever produce signed boxed values.
     */
    public static Long tryGetIntegral(Object value) {
        return switch (value) {
            case Byte v -> v.longValue();
            case Short v -> v.longValue();
            case Integer v -> v.longValue();
            case Long v -> v;
            case String v -> {
                try {
                    yield Long.parseLong(v.trim());
                } catch (NumberFormatException e) {
                    yield null;
                }
            }
            case null, default -> null;
        };
    }
}
