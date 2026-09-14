namespace Foundgine.Core.Abstractions;

/// <summary>
/// Optional named refinement of an <see cref="AuthorizationOperation"/> for
/// policies that distinguish domain-specific write intents (for example
/// "Invoice.Pay" versus "Invoice.Update") beyond the coarse Read/Write gate.
/// This is a policy-facing hint only: it never changes what
/// <see cref="AuthorizationOperation"/> means structurally, and it carries no
/// storage or provider semantics.
/// </summary>
public readonly record struct AuthorizationOperationName(string Value)
{
    public override string ToString() => Value;
}
