package com.foundgine.core.semantic.aggregates;

import java.util.*;

/** Outcome of an aggregate rewrite legality check. */
public record AggregateRewriteLegalityResult(boolean isLegal, List<String> violations) {
    public AggregateRewriteLegalityResult { violations = violations == null ? List.of() : List.copyOf(violations); }
    public static final AggregateRewriteLegalityResult LEGAL = new AggregateRewriteLegalityResult(true, List.of());
    public static AggregateRewriteLegalityResult illegal(String... violations) {
        if (violations == null || violations.length == 0) throw new IllegalArgumentException("An illegal result must carry at least one violation.");
        return new AggregateRewriteLegalityResult(false, List.of(violations));
    }
    public static AggregateRewriteLegalityResult combine(AggregateRewriteLegalityResult... results) {
        var violations = Arrays.stream(results).filter(r -> !r.isLegal()).flatMap(r -> r.violations().stream()).toList();
        return violations.isEmpty() ? LEGAL : new AggregateRewriteLegalityResult(false, violations);
    }
}
