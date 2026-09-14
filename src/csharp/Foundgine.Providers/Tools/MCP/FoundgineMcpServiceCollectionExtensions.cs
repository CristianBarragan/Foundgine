using Foundgine.Core.Execution;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic.Security.Execution;
using Microsoft.Extensions.DependencyInjection;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Tools.MCP;

/// <summary>
/// DI registration for the Foundgine MCP adapter. The application remains
/// responsible for configuring the MCP transport and for supplying execution
/// context such as tenant and caller identity.
/// </summary>
public static class FoundgineMcpServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    /// <param name="contextFactory">Supplies execution context values per call.</param>
    /// <param name="securityContextProvider">
    /// Host-owned source of the caller's <see cref="SecurityExecutionContext"/>. Prefer this
    /// over <paramref name="securityContextFactory"/> for new hosts; both may not be supplied
    /// together.
    /// </param>
    /// <param name="securityContextFactory">
    /// Obsolete delegate form of <paramref name="securityContextProvider"/>, retained for
    /// existing hosts.
    /// </param>
    public static IServiceCollection AddFoundgineMcp(
        this IServiceCollection services,
        Func<ExecutionContext>? contextFactory = null,
        ISecurityExecutionContextProvider? securityContextProvider = null,
        Func<SecurityExecutionContext?>? securityContextFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (securityContextProvider is not null && securityContextFactory is not null)
            throw new ArgumentException(
                $"Only one of {nameof(securityContextProvider)} or {nameof(securityContextFactory)} may be supplied.",
                nameof(securityContextFactory));

        services.AddSingleton<JsonReadIntentAdapter>();
        services.AddSingleton<FoundgineMcpTools>();
        services.AddSingleton<FoundgineMcpMutationTools>();

        if (contextFactory is not null)
            services.AddSingleton(contextFactory);

        if (securityContextProvider is not null)
            services.AddSingleton(securityContextProvider);
        else if (securityContextFactory is not null)
            services.AddSingleton<ISecurityExecutionContextProvider>(
                new DelegateSecurityExecutionContextProvider(securityContextFactory));

        return services;
    }
}