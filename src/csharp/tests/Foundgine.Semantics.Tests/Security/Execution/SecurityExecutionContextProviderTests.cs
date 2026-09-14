using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security.Execution;

public sealed class SecurityExecutionContextProviderTests
{
    private static SecurityWarrant CreateWarrant()
    {
        var now = DateTimeOffset.UtcNow;
        return new SecurityWarrant(
            "warrant-1", "issuer", "subject-1", "api",
            [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
            new SecurityWarrantConstraints(allowedTenants: ["tenant-a"], allowedOperations: ["read"]),
            now.AddMinutes(-1), now.AddMinutes(10), "nonce-1", "issuer-key", null, []);
    }

    private static Foundgine.Core.Semantic.Security.Execution.SecurityExecutionContext CreateContext() =>
        new(CreateWarrant(), "subject-1", "api", "tenant-a", "customer/*");

    private sealed class FixedProvider(Foundgine.Core.Semantic.Security.Execution.SecurityExecutionContext? context)
        : ISecurityExecutionContextProvider
    {
        public Foundgine.Core.Semantic.Security.Execution.SecurityExecutionContext? GetSecurityExecutionContext() =>
            context;
    }

    [Fact]
    public void RequireSecurityExecutionContext_returns_context_when_present()
    {
        var context = CreateContext();
        var provider = new FixedProvider(context);

        var result = provider.RequireSecurityExecutionContext("GraphQL", "execution");

        Assert.Same(context, result);
    }

    [Fact]
    public void RequireSecurityExecutionContext_throws_when_missing()
    {
        var provider = new FixedProvider(null);

        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            provider.RequireSecurityExecutionContext("GraphQL", "execution"));

        Assert.Contains("GraphQL", ex.Message, StringComparison.Ordinal);
        Assert.Contains("execution", ex.Message, StringComparison.Ordinal);
        Assert.Contains("SecurityExecutionContext", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireSecurityExecutionContext_message_identifies_transport_and_operation_distinctly()
    {
        var provider = new FixedProvider(null);

        var mcpEx = Assert.Throws<UnauthorizedAccessException>(() =>
            provider.RequireSecurityExecutionContext("MCP", "capability discovery"));
        var graphQlEx = Assert.Throws<UnauthorizedAccessException>(() =>
            provider.RequireSecurityExecutionContext("GraphQL", "mutation execution"));

        Assert.NotEqual(mcpEx.Message, graphQlEx.Message);
        Assert.Contains("capability discovery", mcpEx.Message, StringComparison.Ordinal);
        Assert.Contains("mutation execution", graphQlEx.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireSecurityExecutionContext_throws_ArgumentNullException_for_null_provider()
    {
        ISecurityExecutionContextProvider? provider = null;

        Assert.Throws<ArgumentNullException>(() => provider!.RequireSecurityExecutionContext("GraphQL", "execution"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RequireSecurityExecutionContext_throws_ArgumentException_for_blank_transportName(string? transportName)
    {
        var provider = new FixedProvider(CreateContext());

        Assert.Throws<ArgumentException>(() => provider.RequireSecurityExecutionContext(transportName!, "execution"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RequireSecurityExecutionContext_throws_ArgumentException_for_blank_operationDescription(
        string? operationDescription)
    {
        var provider = new FixedProvider(CreateContext());

        Assert.Throws<ArgumentException>(() =>
            provider.RequireSecurityExecutionContext("GraphQL", operationDescription!));
    }
}

public sealed class DelegateSecurityExecutionContextProviderTests
{
    private static SecurityWarrant CreateWarrant()
    {
        var now = DateTimeOffset.UtcNow;
        return new SecurityWarrant(
            "warrant-1", "issuer", "subject-1", "api",
            [new CapabilityGrant("Customer.read", "read", ["customer/*"])],
            new SecurityWarrantConstraints(allowedTenants: ["tenant-a"], allowedOperations: ["read"]),
            now.AddMinutes(-1), now.AddMinutes(10), "nonce-1", "issuer-key", null, []);
    }

    [Fact]
    public void Delegates_to_the_supplied_factory_on_each_call()
    {
        var callCount = 0;
        var context = new Foundgine.Core.Semantic.Security.Execution.SecurityExecutionContext(
            CreateWarrant(), "subject-1", "api", "tenant-a", "customer/*");

        var provider = new DelegateSecurityExecutionContextProvider(() =>
        {
            callCount++;
            return context;
        });

        var first = provider.GetSecurityExecutionContext();
        var second = provider.GetSecurityExecutionContext();

        Assert.Same(context, first);
        Assert.Same(context, second);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void Passes_through_null_from_the_factory()
    {
        var provider = new DelegateSecurityExecutionContextProvider(() => null);

        Assert.Null(provider.GetSecurityExecutionContext());
    }

    [Fact]
    public void Constructor_throws_ArgumentNullException_for_null_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new DelegateSecurityExecutionContextProvider(null!));
    }

    [Fact]
    public void Exceptions_from_the_factory_are_not_swallowed()
    {
        var provider = new DelegateSecurityExecutionContextProvider(() =>
            throw new InvalidOperationException("authentication middleware failed"));

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetSecurityExecutionContext());
        Assert.Equal("authentication middleware failed", ex.Message);
    }
}