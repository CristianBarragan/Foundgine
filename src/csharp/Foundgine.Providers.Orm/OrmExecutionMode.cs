namespace Foundgine.Providers.Orm;

/// <summary>
/// How an ORM provider is allowed to obtain the typed expressions it hands to the ORM.
/// </summary>
public enum OrmExecutionMode
{
    /// <summary>
    /// Generated composition (default): per-field typed expression factories are emitted at compile time and
    /// composed at runtime with <see cref="System.Linq.Expressions.Expression"/> combinators only.
    /// No reflection-based property discovery, no <c>Expression.Compile()</c>, no dynamic assemblies.
    /// Anything outside the generated capability set fails explicitly. This is the Native AOT mode.
    /// </summary>
    GeneratedComposition = 0
}

/// <summary>Declares what the ORM execution core can run, so unsupported shapes fail closed and visibly.</summary>
public static class OrmCapabilities
{
    public static readonly IReadOnlyList<string> Supported =
    [
        "scan",
        "filter:eq|neq|in",
        "filter:and|or",
        "authorization:predicate-preserved",
        "authorization:field-visibility",
        "order:node-local-fields",
        "paging:limit|offset|forward-has-next",
        "paging:cursor-after (keyset; orderable, non-null key fields; root node only)",
        "traversal:relationship-children (any depth, either direction, one query per node, joined-row output)",
        "mutation:create|update|delete|upsert (one transaction, filtered updates/deletes only, key back-references)"
    ];

    public static readonly IReadOnlyList<string> NotYetSupported =
    [
        "relationship and aggregate filters",
        "aggregate ordering",
        "per-parent child paging (limit/offset/cursor on child nodes)",
        "connections",
        "cursor paging over Guid/bool/array fields or over null ordering values",
        "mutation security invariants beyond parameterization and atomicity (ownership, idempotency, row locking, ...)"
    ];
}
