package com.foundgine.providers.gateway;

import com.foundgine.providers.gateway.events.IFoundgineGatewayEventStore;
import java.util.ArrayList;
import java.util.List;

/**
 * Host-start validation: negotiates declared requirements against declared provider capabilities, then publishes
 * the providers to the registry. Java counterpart of the .NET {@code FoundgineProviderStartupValidator}.
 */
public final class FoundgineProviderStartup {
    private FoundgineProviderStartup() { }

    public static void validateAndRegister(List<? extends IFoundgineProvider> providers,
            List<FoundgineProviderRequirement> requirements, IFoundgineGatewayEventStore eventStore,
            IFoundgineGatewayProviderRegistry registry) {
        if (providers == null || requirements == null || registry == null)
            throw new IllegalArgumentException("providers, requirements and registry are required");
        var all = new ArrayList<IFoundgineProvider>(providers);
        // The configured journal may declare itself without a separate registration.
        if (eventStore instanceof IFoundgineProvider journal
                && all.stream().noneMatch(p -> p.id().equalsIgnoreCase(journal.id())))
            all.add(journal);

        FoundgineProviderNegotiator.negotiate(all, requirements).throwIfUnsatisfied();

        for (var provider : all)
            if (registry.tryGet(provider.id()).isEmpty()) registry.register(FoundgineGatewayProviderDescriptor.from(provider));
    }
}
