namespace Foundgine.Core.Semantic.Security.Execution;

/// <summary>
/// Adapts a <see cref="Func{TResult}"/> into <see cref="ISecurityExecutionContextProvider"/>.
/// Useful for hosts that already construct the security context via a factory delegate
/// (for example from <c>HttpContext.User</c> inside a request scope), and for adapters
/// migrating an existing <c>Func&lt;SecurityExecutionContext?&gt;</c>-based constructor
/// onto the shared provider contract without breaking existing callers.
/// </summary>
public sealed class DelegateSecurityExecutionContextProvider : ISecurityExecutionContextProvider
{
    private readonly Func<SecurityExecutionContext?> _factory;

    public DelegateSecurityExecutionContextProvider(Func<SecurityExecutionContext?> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public SecurityExecutionContext? GetSecurityExecutionContext() => _factory();
}