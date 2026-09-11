package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import java.util.*;

/** Canonical provider-neutral representation of one mutation operation. */
public record SemanticMutationOperation(EntityId entity, SemanticMutationKind kind,
                                         List<SemanticMutationField> fields, SemanticFilterExpression filter,
                                         List<FieldId> conflictFields, List<FieldId> returnFields,
                                         List<SemanticMutationEffect> effects, List<SemanticMutationDependency> dependencies) {
    public SemanticMutationOperation {
        Objects.requireNonNull(entity); Objects.requireNonNull(kind);
        fields = fields == null ? List.of() : List.copyOf(fields);
        conflictFields = conflictFields == null ? List.of() : List.copyOf(conflictFields);
        returnFields = returnFields == null ? List.of() : List.copyOf(returnFields);
        effects = effects == null ? List.of() : List.copyOf(effects);
        dependencies = dependencies == null ? List.of() : List.copyOf(dependencies);
    }
}
