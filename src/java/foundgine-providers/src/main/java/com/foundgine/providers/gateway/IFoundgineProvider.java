package com.foundgine.providers.gateway;
/** Foundgine-owned provider identity and capability declaration. */
public interface IFoundgineProvider {
    String id(); String name(); String version(); FoundgineProviderKind kind(); long capabilities();
}
