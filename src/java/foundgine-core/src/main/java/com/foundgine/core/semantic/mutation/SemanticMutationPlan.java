package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import java.util.*;

/** Canonical provider-neutral planning artifact for a semantic mutation graph. */
public record SemanticMutationPlan(List<SemanticMutationOperationPlan> operations,
                                   List<SemanticMutationDependencyPlan> dependencies,
                                   List<String> requiredSecurityInvariants) {
    public SemanticMutationPlan {
        operations = operations == null ? List.of() : List.copyOf(operations);
        dependencies = dependencies == null ? List.of() : List.copyOf(dependencies);
        requiredSecurityInvariants = requiredSecurityInvariants == null ? List.of() : List.copyOf(requiredSecurityInvariants);
    }
    public SemanticMutationPlan(List<SemanticMutationOperationPlan> operations, List<SemanticMutationDependencyPlan> dependencies) {
        this(operations,dependencies,List.of());
    }
    public record SemanticMutationOperationPlan(String operationId, EntityId entity, SemanticMutationKind kind,
                                                List<SemanticMutationField> fields, SemanticFilterExpression filter,
                                                List<FieldId> conflictFields, List<FieldId> returnFields,
                                                List<SemanticMutationEffect> effects) {
        public SemanticMutationOperationPlan { fields=List.copyOf(fields); conflictFields=List.copyOf(conflictFields); returnFields=List.copyOf(returnFields); effects=List.copyOf(effects); }
    }
    public record SemanticMutationDependencyPlan(String fromOperationId,String toOperationId,FieldId sourceField,FieldId targetField,RelationshipId relationship) {
        public SemanticMutationDependencyPlan { Objects.requireNonNull(fromOperationId);Objects.requireNonNull(toOperationId);Objects.requireNonNull(sourceField);Objects.requireNonNull(targetField); }
        public SemanticMutationDependencyPlan(String fromOperationId,String toOperationId,FieldId sourceField,FieldId targetField){this(fromOperationId,toOperationId,sourceField,targetField,null);}
    }
}
