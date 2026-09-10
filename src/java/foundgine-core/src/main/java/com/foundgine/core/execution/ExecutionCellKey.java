package com.foundgine.core.execution;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;

/** Port of {@code Foundgine.Core.Execution.ExecutionCellKey} (a C# {@code readonly record struct}). */
public record ExecutionCellKey(int nodeId, EntityId entityId, FieldId fieldId) {
}
