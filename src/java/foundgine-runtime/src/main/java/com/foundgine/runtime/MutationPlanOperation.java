package com.foundgine.runtime;

import java.util.List;

/** Port of the {@code MutationPlanOperation} record declared alongside {@code IFoundgineMutations}. */
public record MutationPlanOperation(
        int index, String entity, String kind, List<String> fields, List<String> returnFields) {

    public MutationPlanOperation {
        fields = fields == null ? List.of() : List.copyOf(fields);
        returnFields = returnFields == null ? List.of() : List.copyOf(returnFields);
    }
}
