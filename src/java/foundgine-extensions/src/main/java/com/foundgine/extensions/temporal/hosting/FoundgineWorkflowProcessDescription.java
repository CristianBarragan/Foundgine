package com.foundgine.extensions.temporal.hosting;

import java.util.List;

/** Public process metadata; never includes operation identifiers. */
public record FoundgineWorkflowProcessDescription(String name, List<FoundgineWorkflowStepDescription> steps) {
    public FoundgineWorkflowProcessDescription { steps = List.copyOf(steps); }
}
