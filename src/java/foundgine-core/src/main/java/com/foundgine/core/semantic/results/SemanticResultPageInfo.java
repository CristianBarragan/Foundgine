package com.foundgine.core.semantic.results;

/** Semantic pagination state, independent of a transport protocol. */
public record SemanticResultPageInfo(
        String startCursor, String endCursor, boolean hasNextPage, boolean hasPreviousPage) { }

