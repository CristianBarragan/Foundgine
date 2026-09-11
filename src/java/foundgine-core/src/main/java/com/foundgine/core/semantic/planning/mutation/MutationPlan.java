package com.foundgine.core.semantic.planning.mutation;
import java.util.*;
public record MutationPlan(List<MutationOperation> operations){ public MutationPlan{operations=List.copyOf(operations);} }
