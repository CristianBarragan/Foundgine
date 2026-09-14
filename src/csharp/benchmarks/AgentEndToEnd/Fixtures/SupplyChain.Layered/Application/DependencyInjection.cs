using Microsoft.Extensions.DependencyInjection;

namespace Foundgine.SupplyChain.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddSupplyChainApplication(this IServiceCollection services)
    {
        services.AddSingleton<ICapabilityAuthorizer, SupplyChainAuthorizer>();
        services.AddScoped<SupplyChainApplication>();
        return services;
    }
}