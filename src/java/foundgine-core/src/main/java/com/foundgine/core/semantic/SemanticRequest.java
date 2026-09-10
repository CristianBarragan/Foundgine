package com.foundgine.core.semantic;
import com.foundgine.core.abstractions.*; import com.foundgine.core.semantic.query.*; import com.foundgine.core.semantic.security.execution.SecurityExecutionContext; import java.util.*;
/** Protocol-neutral description of caller intent. */
public record SemanticRequest(EntityId root,List<SemanticSelection> selections,SemanticQueryOptions options,SecurityExecutionContext security){public SemanticRequest{Objects.requireNonNull(root);selections=List.copyOf(selections==null?List.of():selections);}}
