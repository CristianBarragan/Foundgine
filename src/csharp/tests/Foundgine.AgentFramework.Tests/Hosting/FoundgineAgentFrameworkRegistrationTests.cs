using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Providers.AgentFramework.Hosting;
using Foundgine.Providers.AgentFramework.Tools;
using Foundgine.Providers.Gateway.Providers;
using Foundgine.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Foundgine.AgentFramework.Tests.Support;

namespace Foundgine.AgentFramework.Tests.Hosting;

public sealed class FoundgineAgentFrameworkRegistrationTests
{
    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static ServiceCollection Host(bool withSecurity = true, Action<FoundgineAgentFrameworkOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFoundgine>(new StubFoundgine());
        services.AddSingleton<IChatClient>(new NoopChatClient());
        services.AddSingleton<IFoundgineMutations>(new RecordingMutations());
        if (withSecurity)
            services.AddSingleton<ISecurityExecutionContextProvider>(new FixedProvider(Context()));
        services.AddFoundgineAgentFramework(configure);
        return services;
    }

    [Fact]
    public void ProviderIsAnAgentKindWithTenantIsolation()
    {
        var provider = new FoundgineAgentFrameworkProvider();
        Assert.Equal(FoundgineProviderKind.Agent, provider.Kind);
        Assert.True(provider.Capabilities.HasFlag(FoundgineProviderCapability.TenantIsolation));
        Assert.Equal("foundgine.maf.agent", provider.ToDescriptor().Id);
    }

    [Fact]
    public void RegistersTheProviderForGatewayDiscovery()
    {
        using var sp = Host().BuildServiceProvider();
        Assert.Contains(sp.GetServices<IFoundgineProvider>(), p => p.Kind == FoundgineProviderKind.Agent);
    }

    [Fact]
    public void ResolvesAKeyedAgentUnderTheConfiguredName()
    {
        using var sp = Host(configure: o => o.AgentName = "orders").BuildServiceProvider();
        using var scope = sp.CreateScope();

        var agent = scope.ServiceProvider.GetRequiredKeyedService<AIAgent>("orders");

        Assert.Equal("orders", agent.Name);
    }

    [Fact]
    public void FailsClosedWhenNoSecurityContextProviderIsRegistered()
    {
        using var sp = Host(withSecurity: false).BuildServiceProvider();
        using var scope = sp.CreateScope();

        var ex = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<FoundgineAgentTools>());
        Assert.Contains("ISecurityExecutionContextProvider", ex.Message);
    }

    [Fact]
    public void BlankAgentNameIsRejectedAtRegistration() =>
        Assert.Throws<InvalidOperationException>(() => Host(configure: o => o.AgentName = " "));
}
