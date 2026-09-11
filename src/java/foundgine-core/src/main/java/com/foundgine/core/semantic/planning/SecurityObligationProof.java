package com.foundgine.core.semantic.planning;

import com.foundgine.core.semantic.security.SecurityInvariantRegistry;
import java.util.*;

/** Machine-readable evidence that every security obligation declared by a rewrite rule was evaluated. */
public record SecurityObligationProof(List<String> obligations,List<String> preserved,List<String> notRequired,List<String> violations) {
    public SecurityObligationProof {obligations=List.copyOf(obligations);preserved=List.copyOf(preserved);notRequired=List.copyOf(notRequired);violations=List.copyOf(violations);}
    public boolean isSatisfied(){var left=obligations.stream().sorted().toList();var right=new ArrayList<String>();right.addAll(preserved);right.addAll(notRequired);right.sort(Comparator.naturalOrder());return violations.isEmpty()&&left.equals(right);}
    public static SecurityObligationProof create(IPlanRewriteRule rule,SemanticPlan before,SemanticPlan after){
        Objects.requireNonNull(rule);Objects.requireNonNull(before);Objects.requireNonNull(after);var obligations=rule.securityObligations().stream().distinct().sorted().toList();var violations=new ArrayList<String>();for(var obligation:obligations)if(!SecurityInvariantRegistry.contains(obligation))violations.add("Rewrite rule '"+rule.name()+"' declares unknown security obligation '"+obligation+"'.");var beforeSet=new HashSet<>(before.effectiveSecurityInvariants());var afterSet=new HashSet<>(after.effectiveSecurityInvariants());var preserved=new ArrayList<String>();var notRequired=new ArrayList<String>();for(var obligation:obligations){if(!SecurityInvariantRegistry.contains(obligation))continue;if(!beforeSet.contains(obligation)){notRequired.add(obligation);continue;}if(!afterSet.contains(obligation)){violations.add("Rewrite rule '"+rule.name()+"' dropped declared security obligation '"+obligation+"'.");continue;}preserved.add(obligation);}var proof=new SecurityObligationProof(obligations,preserved,notRequired,violations);if(!proof.isSatisfied())throw new IllegalStateException(String.join(" ",violations));return proof;
    }
}
