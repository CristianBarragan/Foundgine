package com.foundgine.core.semantic.results;

import java.util.*;

/** Provider-independent result of a resolved semantic operation. */
public record SemanticResult(
        List<SemanticResultNode> roots,
        SemanticResultPageInfo pageInfo,
        SemanticResultEvidence evidence) {
    public SemanticResult {
        roots = roots == null ? List.of() : List.copyOf(roots);
    }

    public SemanticResult(List<SemanticResultNode> roots) {
        this(roots, null, null);
    }
}
