using Foundgine.Providers.Gateway.Providers;

namespace Foundgine.Extensions.Keycloak.Hosting;

public sealed class FoundgineKeycloakProvider : IFoundgineProvider
{
    public string Id => "foundgine.keycloak.identity";
    public string Name => "Keycloak identity";
    public string Version => "1.0.0";
    public FoundgineProviderKind Kind => FoundgineProviderKind.Identity;
    public FoundgineProviderCapability Capabilities => FoundgineProviderCapability.TenantIsolation;
}
