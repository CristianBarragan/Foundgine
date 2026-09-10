package com.foundgine.core.semantic.planning;

import java.util.*;

/** Records that a rewrite did not weaken the source plan's security contract. */
public record AuthorizationPreservationProof(boolean isSatisfied, List<String> violations) {
    public AuthorizationPreservationProof { violations=violations==null?List.of():List.copyOf(violations); }
    public static AuthorizationPreservationProof create(SemanticPlan before,SemanticPlan after){
        Objects.requireNonNull(before,"before");Objects.requireNonNull(after,"after");
        var afterInvariants=new HashSet<>(after.effectiveSecurityInvariants());var dropped=before.effectiveSecurityInvariants().stream().filter(x->!afterInvariants.contains(x)).toList();
        if(dropped.isEmpty())return new AuthorizationPreservationProof(true,List.of());
        return new AuthorizationPreservationProof(false,dropped.stream().map(id->"security invariant '"+id+"' required by the source plan is not preserved by the rewritten plan.").toList());
    }
}
