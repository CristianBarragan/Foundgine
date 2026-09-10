package com.foundgine.core.semantic.planning.mutation;
import java.util.*;
public record MutationBatchIntent(List<IMutationIntent> operations){ public MutationBatchIntent{operations=List.copyOf(operations);} }
