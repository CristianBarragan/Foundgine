namespace Foundgine.Core.Semantic.Query;

/// <summary>
/// Protocol-neutral query controls, including filters, ordering, limits,
/// offsets, and forward cursor pagination.
/// </summary>
public sealed record SemanticQueryOptions(
    SemanticFilterExpression? Filter = null,
    IReadOnlyList<SemanticOrderTerm>? Order = null,
    int? Limit = null,
    int? Offset = null,
    string? After = null)
{
    public IReadOnlyList<SemanticOrderTerm> EffectiveOrder => Order ?? [];

    public bool HasCursor => !string.IsNullOrWhiteSpace(After);
}