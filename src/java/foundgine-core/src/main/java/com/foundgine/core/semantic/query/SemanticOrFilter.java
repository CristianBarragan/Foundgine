package com.foundgine.core.semantic.query;
import java.util.*;
public record SemanticOrFilter(List<SemanticFilterExpression> expressions) implements SemanticFilterExpression { public SemanticOrFilter{expressions=List.copyOf(expressions);} }
