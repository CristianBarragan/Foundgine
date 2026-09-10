package com.foundgine.core.semantic.aggregates;

import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import java.util.*;

/** Provider-declared capabilities relevant to aggregate rewrites. */
public record AggregateProviderCapability(String providerName,List<SemanticFilterAggregate> supportedAggregates,boolean supportsAggregatePredicate,boolean supportsRelationshipQuantifiers) {
    public AggregateProviderCapability { Objects.requireNonNull(providerName);supportedAggregates=supportedAggregates==null?List.of():List.copyOf(supportedAggregates); }
    public boolean supports(SemanticFilterAggregate aggregate){return supportedAggregates.contains(aggregate);}
}

