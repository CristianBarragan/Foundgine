package com.foundgine.core.semantic.intent;

import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import java.util.*;

/** External, provider-neutral read intent. */
public record ReadIntent(String rootEntity, List<ReadSelection> selections, ReadFilter filter,
                         List<ReadOrder> order, Integer limit, Integer offset, String after,
                         SecurityExecutionContext security) {
    public ReadIntent {
        Objects.requireNonNull(rootEntity, "rootEntity");
        selections = selections == null ? List.of() : List.copyOf(selections);
        order = order == null ? List.of() : List.copyOf(order);
    }
    public ReadIntent(String rootEntity, List<ReadSelection> selections) { this(rootEntity, selections, null, List.of(), null, null, null, null); }
}
