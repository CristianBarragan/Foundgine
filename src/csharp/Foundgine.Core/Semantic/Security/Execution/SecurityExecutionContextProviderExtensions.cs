namespace Foundgine.Core.Semantic.Security.Execution;

/// <summary>
/// Shared null-check helper for <see cref="ISecurityExecutionContextProvider"/>
/// consumers. Centralizing the missing-context failure here means every transport
/// adapter (MCP, GraphQL, future REST/gRPC) reports the same failure the same way,
/// instead of each adapter writing and maintaining its own message.
/// </summary>
public static class SecurityExecutionContextProviderExtensions
{
    /// <summary>
    /// Returns the current <see cref="SecurityExecutionContext"/>, or throws
    /// <see cref="UnauthorizedAccessException"/> if the host has not supplied one.
    /// </summary>
    /// <param name="provider">The host-supplied security context source.</param>
    /// <param name="transportName">
    /// Short transport identifier used in the exception message, e.g. <c>"GraphQL"</c>
    /// or <c>"MCP"</c>.
    /// </param>
    /// <param name="operationDescription">
    /// Short description of the operation being attempted, e.g. <c>"execution"</c>,
    /// <c>"capability discovery"</c>, or <c>"mutation execution"</c>.
    /// </param>
    public static SecurityExecutionContext RequireSecurityExecutionContext(
        this ISecurityExecutionContextProvider provider,
        string transportName,
        string operationDescription)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(transportName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(transportName));
        if (string.IsNullOrWhiteSpace(operationDescription))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(operationDescription));

        return provider.GetSecurityExecutionContext()
               ?? throw new UnauthorizedAccessException(
                   $"{transportName} {operationDescription} requires a host-supplied SecurityExecutionContext. " +
                   $"The {transportName} caller cannot supply identity, tenant, audience, or warrant context.");
    }
}