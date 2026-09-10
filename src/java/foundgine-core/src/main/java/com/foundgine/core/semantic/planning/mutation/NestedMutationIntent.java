package com.foundgine.core.semantic.planning.mutation;
import com.foundgine.core.abstractions.RelationshipId; import java.util.*;
public record NestedMutationIntent(IMutationIntent mutation,List<NestedMutationChild> children){public NestedMutationIntent{Objects.requireNonNull(mutation);children=children==null?List.of():List.copyOf(children);}}
