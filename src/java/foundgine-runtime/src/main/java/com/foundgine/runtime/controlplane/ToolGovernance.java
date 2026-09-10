package com.foundgine.runtime.controlplane;

import java.util.function.Consumer;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.ToolGovernanceServiceCollectionExtensions.AddFoundgineToolGovernance},
 * the static entry point. See {@link ToolGovernanceBuilder} for the porting
 * decision replacing DI registration with direct assembly.
 */
public final class ToolGovernance {
    private ToolGovernance() {
    }

    public static ToolCallGovernor create() {
        return create(null);
    }

    public static ToolCallGovernor create(Consumer<ToolGovernanceBuilder> configure) {
        var builder = new ToolGovernanceBuilder();
        if (configure != null) {
            configure.accept(builder);
        }
        return builder.build();
    }
}
