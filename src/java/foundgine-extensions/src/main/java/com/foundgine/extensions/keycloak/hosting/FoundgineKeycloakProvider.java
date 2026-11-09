package com.foundgine.extensions.keycloak.hosting;

import com.foundgine.providers.gateway.FoundgineProviderCapability;
import com.foundgine.providers.gateway.FoundgineProviderKind;
import com.foundgine.providers.gateway.IFoundgineProvider;

/** Registers Keycloak as an Identity provider with the Foundgine provider registry. */
public final class FoundgineKeycloakProvider implements IFoundgineProvider {
    @Override public String id() { return "foundgine.keycloak.identity"; }
    @Override public String name() { return "Keycloak identity"; }
    @Override public String version() { return "1.0.0"; }
    @Override public FoundgineProviderKind kind() { return FoundgineProviderKind.IDENTITY; }
    @Override public long capabilities() { return FoundgineProviderCapability.mask(FoundgineProviderCapability.TENANT_ISOLATION); }
}
