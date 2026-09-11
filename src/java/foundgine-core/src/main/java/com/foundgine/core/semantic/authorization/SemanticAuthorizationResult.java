package com.foundgine.core.semantic.authorization;
import com.foundgine.core.semantic.ir.SemanticOperation; import com.foundgine.core.semantic.SemanticContractSnapshot; import java.util.*;
public record SemanticAuthorizationResult(SemanticOperation operation,SemanticAuthorizationEvidence evidence){public SemanticAuthorizationResult{Objects.requireNonNull(operation);Objects.requireNonNull(evidence);}public void ensureMatches(SemanticContractSnapshot c){evidence.ensureMatches(c);}}
