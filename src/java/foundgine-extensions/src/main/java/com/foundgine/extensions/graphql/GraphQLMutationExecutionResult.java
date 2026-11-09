package com.foundgine.extensions.graphql;

import com.foundgine.runtime.MutationExecutionResult;
import java.util.Map;

/**
 * Outcome of a secured GraphQL mutation execution: the engine's execution result plus the GraphQL {@code data}
 * object keyed by response key (a key maps to {@code null} when its root affected no rows).
 */
public record GraphQLMutationExecutionResult(MutationExecutionResult execution, Map<String, Object> data) { }
