package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import java.util.List;

/** Central registry of provider aggregate capabilities. */
public final class AggregateProviderCapabilityRegistry {
    private AggregateProviderCapabilityRegistry() {}

    /** Generic SQL provider: all catalogued aggregates, aggregate predicates and quantifiers. */
    public static final AggregateProviderCapability GENERIC_SQL =
            new AggregateProviderCapability("sql", List.of(
                    SemanticFilterAggregate.COUNT,
                    SemanticFilterAggregate.MIN,
                    SemanticFilterAggregate.MAX), true, true);
}
