package com.foundgine.extensions.graphql;

import com.foundgine.core.semantic.SemanticRequest;

/** A translated query: the provider-neutral request plus the shape used to project results back to GraphQL. */
public record GraphQLQueryAdaptation(SemanticRequest request, GraphQLResultShape result) { }
