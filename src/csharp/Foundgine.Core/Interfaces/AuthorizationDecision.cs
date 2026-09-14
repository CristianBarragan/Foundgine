namespace Foundgine.Core.Abstractions;

/// <summary>Operation for which a semantic authorization decision is requested.</summary>
public enum AuthorizationOperation : byte
{
    Read,
    Write
}

/// <summary>High-level authorization state exposed by the semantic capability model.</summary>
public enum AuthorizationAccess : byte
{
    Denied,
    Allowed,
    Conditional
}

/// <summary>
/// Provider-independent authorization result. Conditional access carries a
/// predicate that must remain part of execution semantics and be evaluated at
/// the provider boundary with the current execution context.
/// </summary>
public sealed record AuthorizationDecision(
    AuthorizationAccess Access,
    AuthorizationPredicate? Predicate = null)
{
    public static AuthorizationDecision Denied { get; } = new(AuthorizationAccess.Denied);
    public static AuthorizationDecision Allowed { get; } = new(AuthorizationAccess.Allowed);

    public static AuthorizationDecision Conditional(AuthorizationPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new AuthorizationDecision(AuthorizationAccess.Conditional, predicate);
    }

    public bool IsAllowed => Access is AuthorizationAccess.Allowed or AuthorizationAccess.Conditional;

    public static AuthorizationDecision Combine(AuthorizationDecision left, AuthorizationDecision right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!left.IsAllowed || !right.IsAllowed)
            return Denied;

        if (left.Predicate is null)
            return right;

        if (right.Predicate is null)
            return left;

        return Conditional(AuthorizationPredicate.And(left.Predicate, right.Predicate));
    }
}