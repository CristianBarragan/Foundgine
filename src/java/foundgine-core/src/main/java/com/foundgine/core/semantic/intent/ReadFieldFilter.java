package com.foundgine.core.semantic.intent;

import com.foundgine.core.semantic.query.SemanticFilterOperator;

public record ReadFieldFilter(String field, SemanticFilterOperator operator, Object value) implements ReadFilter {
}
