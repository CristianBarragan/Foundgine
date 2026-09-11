package com.foundgine.core.semantic.planning;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;
import java.util.*;
public record SemanticPlanAuthorizationBinding(String contractFingerprint,String authorizationFingerprint) {
 public static SemanticPlanAuthorizationBinding create(SemanticContractSnapshot c, SemanticAuthorizationEvidence e){Objects.requireNonNull(c);Objects.requireNonNull(e);e.ensureMatches(c);return new SemanticPlanAuthorizationBinding(c.contractFingerprint(),e.authorizationFingerprint());}
 public void ensureMatches(SemanticContractSnapshot c,SemanticAuthorizationEvidence e){Objects.requireNonNull(c);Objects.requireNonNull(e);if(!contractFingerprint.equals(c.contractFingerprint()))throw new IllegalStateException("Semantic plan is bound to a different contract.");if(!authorizationFingerprint.equals(e.authorizationFingerprint()))throw new IllegalStateException("Semantic plan authorization evidence does not match.");e.ensureMatches(c);}
}
