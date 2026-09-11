package com.foundgine.core.semantic.authorization;
import com.foundgine.core.semantic.*; import com.foundgine.core.semantic.ir.graph.*; import java.util.*;
public record SemanticOperationGraphAuthorizationResult(SemanticOperationGraph graph,SemanticAuthorizationEvidence evidence){public SemanticOperationGraphAuthorizationResult{Objects.requireNonNull(graph);Objects.requireNonNull(evidence);}public void ensureMatches(SemanticContractSnapshot c){evidence.ensureMatches(c);}}
