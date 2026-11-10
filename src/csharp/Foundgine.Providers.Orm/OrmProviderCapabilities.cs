namespace Foundgine.Providers.Orm;

/// <summary>Read-side execution features understood by the shared ORM compiler.</summary>
[Flags]
public enum OrmProviderFeature
{
    None = 0,
    Scan = 1 << 0,
    RelationshipTraversal = 1 << 1,
    Filtering = 1 << 2,
    Projection = 1 << 3,
    Ordering = 1 << 4,
    OffsetPagination = 1 << 5,
    CursorPagination = 1 << 6,
    AuthorizationPredicates = 1 << 7,
    RelationshipFilters = 1 << 8,
    AggregateFilters = 1 << 9,
    AggregateOrdering = 1 << 10,
    PerParentPagination = 1 << 11,
    AllRead = Scan | RelationshipTraversal | Filtering | Projection | Ordering |
              OffsetPagination | CursorPagination | AuthorizationPredicates,
    All = AllRead | RelationshipFilters | AggregateFilters | AggregateOrdering | PerParentPagination
}

/// <summary>
/// Explicit, versioned declaration of a provider's supported read shapes. This is a translation
/// capability declaration, not a security certification: security invariants are evaluated separately.
/// </summary>
public sealed record OrmProviderCapabilities(
    string Provider,
    string ContractVersion,
    OrmProviderFeature Features,
    IReadOnlyList<string>? Notes = null)
{
    public OrmProviderCapabilities ValidateFor(string provider)
    {
        if (string.IsNullOrWhiteSpace(Provider))
            throw new ArgumentException("An ORM capability profile must identify its provider.");
        if (string.IsNullOrWhiteSpace(ContractVersion))
            throw new ArgumentException("An ORM capability profile must declare a contract version.");
        if (!string.Equals(Provider, provider, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"ORM capability profile '{Provider}' cannot be used by provider '{provider}'.");
        if ((Features & ~OrmProviderFeature.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(Features), "The capability profile contains unknown features.");
        return this;
    }

    public void EnsureSupports(OrmProviderFeature required)
    {
        var missing = required & ~Features;
        if (missing != OrmProviderFeature.None)
            throw new NotSupportedException(
                $"ORM provider '{Provider}' does not support required execution features: {missing}.");
    }

    /// <summary>Baseline for the currently implemented shared LINQ execution path.</summary>
    public static OrmProviderCapabilities SharedLinq(string provider) => new(
        provider,
        "2",
        OrmProviderFeature.AllRead,
        ["Expression translation is validated by provider-specific database tests; this profile is not an AOT claim."]);
}
