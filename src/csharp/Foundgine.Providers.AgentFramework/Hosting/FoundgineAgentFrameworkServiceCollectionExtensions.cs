using Foundgine.Core.Semantic.Resolution;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Serialization;
using Foundgine.Providers.AgentFramework.Agents;
using Foundgine.Providers.AgentFramework.Tools;
using Foundgine.Providers.Gateway.Providers;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.AgentFramework.Hosting;

public static class FoundgineAgentFrameworkServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Foundgine MAF adapter: the <see cref="FoundgineAgentFrameworkProvider"/> descriptor, a scoped
    /// <see cref="FoundgineAgentTools"/>, and a scoped <see cref="AIAgent"/> keyed by <c>options.AgentName</c>.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="IFoundgine"/>, <see cref="IChatClient"/> and an <see cref="ISecurityExecutionContextProvider"/>
    /// in the container. Everything is scoped so a per-request security context is never captured by a singleton.
    /// Resolve with <c>GetRequiredKeyedService&lt;AIAgent&gt;(name)</c>.
    /// </remarks>
    public static IServiceCollection AddFoundgineAgentFramework(
        this IServiceCollection services,
        Action<FoundgineAgentFrameworkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new FoundgineAgentFrameworkOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.TryAddSingleton<JsonReadIntentAdapter>();
        services.AddFoundgineProvider<FoundgineAgentFrameworkProvider>();

        services.TryAddScoped(sp =>
        {
            var security = sp.GetService<ISecurityExecutionContextProvider>()
                           ?? throw new InvalidOperationException(
                               "AddFoundgineAgentFramework requires an ISecurityExecutionContextProvider. " +
                               "The model can never supply identity, tenant, audience or warrant context.");
            var grounder = sp.GetService<SemanticLexicalReadIntentGrounder>();

            var reads = new FoundgineMcpTools(
                sp.GetRequiredService<IFoundgine>(),
                sp.GetRequiredService<JsonReadIntentAdapter>(),
                sp.GetService<Func<ExecutionContext>>(),
                securityContextProvider: security,
                lexicalGrounder: grounder);

            return new FoundgineAgentTools(
                reads, security, sp.GetService<IFoundgineMutations>(), openIntentAvailable: grounder is not null);
        });

        services.AddKeyedScoped<AIAgent>(options.AgentName, (sp, _) =>
            FoundgineAgentFactory.Create(
                sp.GetRequiredService<IChatClient>(),
                sp.GetRequiredService<FoundgineAgentTools>(),
                sp.GetRequiredService<FoundgineAgentFrameworkOptions>()));

        return services;
    }
}
