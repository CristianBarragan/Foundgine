package com.foundgine.extensions.temporal.contracts;

import java.util.List;

/** Server-side definition. Clients choose a process by name and never submit operations. */
public record FoundgineProcessDefinition(String name, List<FoundgineProcessStep> steps) {
    public FoundgineProcessDefinition {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Process name is required.");
        steps = List.copyOf(steps);
    }
}
