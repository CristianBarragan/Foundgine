package com.foundgine.core.semantic.planning.algebra;

import com.foundgine.core.semantic.query.*;
import java.util.*;

/** Canonical composition helpers for semantic query predicates. */
public final class SemanticPredicateAlgebra {
    private SemanticPredicateAlgebra() {}
    public static SemanticFilterExpression and(SemanticFilterExpression... predicates){ return combine(true,predicates); }
    public static SemanticFilterExpression or(SemanticFilterExpression... predicates){ return combine(false,predicates); }
    private static SemanticFilterExpression combine(boolean and, SemanticFilterExpression... predicates){
        Objects.requireNonNull(predicates); var terms=Arrays.stream(predicates).filter(Objects::nonNull).toList();
        if(terms.isEmpty()) throw new IllegalArgumentException("At least one predicate is required.");
        if(terms.size()==1) return terms.get(0);
        return and?new SemanticAndFilter(terms):new SemanticOrFilter(terms);
    }
}
