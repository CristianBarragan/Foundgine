namespace Foundgine.Core.Semantic.Security.Execution;

/// <summary>
/// Host-owned source of the caller's <see cref="SecurityExecutionContext"/> for a
/// single request. Transport adapters (GraphQL, MCP, REST, or any future adapter)
/// depend on this contract instead of each independently agreeing on an ad-hoc
/// delegate shape, so every transport converges on the same host-owned security
/// boundary before a request reaches Foundgine.
///
/// Adapters must never accept identity, tenant, audience, or warrant material from
/// the untrusted request payload itself. The host is responsible for establishing
/// this context from its own authentication mechanism (for example
/// <c>HttpContext.User</c>, an <c>IResolverContext</c>, or an MCP transport's
/// authenticated session) before translation or execution begins.
/// </summary>
public interface ISecurityExecutionContextProvider
{
    /// <summary>
    /// Returns the security context for the current request, or <see langword="null"/>
    /// if the host has not established one. A <see langword="null"/> result means no
    /// trusted security context is available — whether because the caller is
    /// unauthenticated, authentication middleware was not invoked, or the host is
    /// otherwise misconfigured. Foundgine must fail closed in every such case; adapters
    /// must refuse to execute rather than proceed without a context. Prefer
    /// <see cref="SecurityExecutionContextProviderExtensions.RequireSecurityExecutionContext"/>
    /// over inlining that null check.
    /// </summary>
    SecurityExecutionContext? GetSecurityExecutionContext();
}
