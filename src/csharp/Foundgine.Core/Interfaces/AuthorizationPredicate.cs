namespace Foundgine.Core.Abstractions;

/// <summary>
/// Small provider-independent representation of an AOT authorization predicate.
/// It contains no expression trees and no executable delegates.
/// </summary>
public sealed record AuthorizationPredicate(
    AuthorizationPredicateKind Kind,
    string? Name = null,
    string? Value = null,
    AuthorizationPredicate? Left = null,
    AuthorizationPredicate? Right = null)
{
    public static AuthorizationPredicate Parameter(string name) =>
        new(AuthorizationPredicateKind.Parameter, Name: name);

    public static AuthorizationPredicate ContextParameter(string name) =>
        new(AuthorizationPredicateKind.ContextParameter, Name: name);

    public static AuthorizationPredicate ResourceParameter(string name) =>
        new(AuthorizationPredicateKind.ResourceParameter, Name: name);

    public static AuthorizationPredicate Member(AuthorizationPredicate target, string name) =>
        new(AuthorizationPredicateKind.MemberAccess, Name: name, Left: target);

    public static AuthorizationPredicate Constant(string value) =>
        new(AuthorizationPredicateKind.Constant, Value: value);

    public static AuthorizationPredicate Equal(AuthorizationPredicate left, AuthorizationPredicate right) =>
        new(AuthorizationPredicateKind.Equal, Left: left, Right: right);

    public static AuthorizationPredicate NotEqual(AuthorizationPredicate left, AuthorizationPredicate right) =>
        new(AuthorizationPredicateKind.NotEqual, Left: left, Right: right);

    public static AuthorizationPredicate And(AuthorizationPredicate left, AuthorizationPredicate right) =>
        new(AuthorizationPredicateKind.And, Left: left, Right: right);

    public static AuthorizationPredicate Or(AuthorizationPredicate left, AuthorizationPredicate right) =>
        new(AuthorizationPredicateKind.Or, Left: left, Right: right);

    public static AuthorizationPredicate Not(AuthorizationPredicate operand) =>
        new(AuthorizationPredicateKind.Not, Left: operand);
}

public enum AuthorizationPredicateKind : byte
{
    Parameter,
    ContextParameter,
    ResourceParameter,
    MemberAccess,
    Constant,
    Equal,
    NotEqual,
    And,
    Or,
    Not
}