namespace Foundgine.Core.Semantic;

/// <summary>Provider-neutral semantic constraint attached to a field.</summary>
public sealed record SemanticConstraint(
    SemanticConstraintKind Kind,
    string? Value = null,
    decimal? Minimum = null,
    decimal? Maximum = null)
{
    public static SemanticConstraint Range(decimal? minimum = null, decimal? maximum = null) =>
        new(SemanticConstraintKind.Range, Minimum: minimum, Maximum: maximum);

    public static SemanticConstraint Pattern(string pattern) =>
        new(SemanticConstraintKind.Pattern, pattern);

    public static SemanticConstraint Temporal(string semantics) =>
        new(SemanticConstraintKind.Temporal, semantics);

    public static SemanticConstraint Currency(string currencyCode) =>
        new(SemanticConstraintKind.Currency, currencyCode);

    public static SemanticConstraint CountryCode(string countryCode) =>
        new(SemanticConstraintKind.CountryCode, countryCode);
}

public enum SemanticConstraintKind : byte
{
    Range,
    Pattern,
    Temporal,
    Currency,
    CountryCode
}
