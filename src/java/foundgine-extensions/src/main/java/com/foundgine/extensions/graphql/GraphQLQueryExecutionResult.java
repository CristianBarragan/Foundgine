package com.foundgine.extensions.graphql;

import com.foundgine.core.execution.ExecutionResult;

/**
 * The outcome of a secured GraphQL query execution: the raw provider-neutral execution result plus the GraphQL result
 * shape (aliases, nesting) the host needs to project it into a GraphQL response. Port of
 * {@code GraphQLQueryExecutionResult}.
 */
public record GraphQLQueryExecutionResult(ExecutionResult execution, GraphQLResultShape resultShape) { }
