package com.foundgine.runtime.controlplane.toolregistry;

import java.util.Collection;
import java.util.Optional;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.ToolRegistry.IToolRegistry}.
 *
 * <p><b>Porting decision:</b> {@code bool TryGet(string, out ToolDescriptor?)}
 * is ported as {@link #tryGet(String)} returning {@code Optional<ToolDescriptor>},
 * the same substitution used for {@code IApprovalStore.TryGet}.
 */
public interface IToolRegistry {
    Optional<ToolDescriptor> tryGet(String toolName);

    Collection<ToolDescriptor> listActive();

    void register(ToolDescriptor descriptor);
}
