package com.foundgine.runtime.controlplane.toolregistry;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.ToolRegistry.InMemoryToolRegistry},
 * declared alongside {@code IToolRegistry} in the same C# file.
 *
 * <p>Process-local tool registry. Suitable for a single host instance; a
 * distributed deployment should back {@link IToolRegistry} with a shared
 * store instead, the same substitution pattern used by
 * {@code IProviderPlanCache} / {@code MemoryProviderPlanCache} in
 * {@code Foundgine.Core}.
 *
 * <p><b>Porting decision:</b> the C# constructor takes an optional
 * {@code IEnumerable<ToolDescriptor>? seed}, immediately registered. Java
 * ports this as two constructors (no-arg, and one taking an explicit seed
 * list) rather than a nullable default parameter. {@code System.Threading.Lock}
 * is ported as a plain {@code Object} monitor guarded with {@code synchronized},
 * consistent with the other in-memory stores in this module.
 */
public final class InMemoryToolRegistry implements IToolRegistry {
    private final Map<String, ToolDescriptor> tools = new HashMap<>();
    private final Object gate = new Object();

    public InMemoryToolRegistry() {
        this(List.of());
    }

    /**
     * @param seed descriptors to register immediately, e.g. from
     *             collected {@code ToolDescriptor} registrations made via
     *             tool-governance builder setup.
     */
    public InMemoryToolRegistry(List<ToolDescriptor> seed) {
        for (var descriptor : seed == null ? List.<ToolDescriptor>of() : seed) {
            register(descriptor);
        }
    }

    @Override
    public Optional<ToolDescriptor> tryGet(String toolName) {
        if (toolName == null || toolName.isBlank()) {
            throw new IllegalArgumentException("toolName is required.");
        }
        synchronized (gate) {
            return Optional.ofNullable(tools.get(toolName));
        }
    }

    @Override
    public List<ToolDescriptor> listActive() {
        synchronized (gate) {
            var result = new ArrayList<ToolDescriptor>();
            for (var descriptor : tools.values()) {
                if (descriptor.status() == ToolDescriptor.ToolStatus.ACTIVE) {
                    result.add(descriptor);
                }
            }
            return List.copyOf(result);
        }
    }

    @Override
    public void register(ToolDescriptor descriptor) {
        Objects.requireNonNull(descriptor, "descriptor");
        synchronized (gate) {
            tools.put(descriptor.toolName(), descriptor);
        }
    }
}
