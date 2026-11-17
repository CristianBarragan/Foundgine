using System.Collections.ObjectModel;

namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>One capability granted by a security warrant.</summary>
public sealed record CapabilityGrant(
    string Capability,
    string Operation,
    IReadOnlyList<string> ResourceScopes)
{
    public CapabilityGrant(string capability, string operation, IEnumerable<string>? resourceScopes = null)
        : this(
            Require(capability, nameof(capability)),
            Require(operation, nameof(operation)),
            Normalize(resourceScopes))
    {
    }

    private static string Require(string value, string name) =>
        WarrantText.IsBlank(value) ? throw new ArgumentException("Value is required.", name) : value;

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? values) =>
        new ReadOnlyCollection<string>((values ?? []).Where(x => !WarrantText.IsBlank(x))
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
}

/// <summary>
/// Explicit, parameterized restrictions carried by a warrant. A child warrant
/// can only remove authority from these constraints, never add it.
/// </summary>
public sealed record SecurityWarrantConstraints(
    IReadOnlyList<string> AllowedTenants,
    IReadOnlyList<string> AllowedFields,
    IReadOnlyList<string> ResourceScopes,
    IReadOnlyList<string> AllowedOperations,
    long? MaxResults,
    decimal? MaxAmount)
{
    public static SecurityWarrantConstraints Unrestricted { get; } =
        new([], [], [], [], null, null);

    public SecurityWarrantConstraints(
        IEnumerable<string>? allowedTenants = null,
        IEnumerable<string>? allowedFields = null,
        IEnumerable<string>? resourceScopes = null,
        IEnumerable<string>? allowedOperations = null,
        long? maxResults = null,
        decimal? maxAmount = null)
        : this(
            Normalize(allowedTenants),
            Normalize(allowedFields),
            Normalize(resourceScopes),
            Normalize(allowedOperations),
            Validate(maxResults, nameof(maxResults)),
            Validate(maxAmount, nameof(maxAmount)))
    {
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? values) =>
        new ReadOnlyCollection<string>((values ?? []).Where(x => !WarrantText.IsBlank(x))
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());

    private static T? Validate<T>(T? value, string name) where T : struct, IComparable<T> =>
        value is { } v && v.CompareTo(default) < 0 ? throw new ArgumentOutOfRangeException(name) : value;

    /// <summary>Returns true when this constraint set is no more powerful than parent.</summary>
    public bool IsAtMostAsPowerfulAs(SecurityWarrantConstraints parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return Subset(AllowedTenants, parent.AllowedTenants)
               && Subset(AllowedFields, parent.AllowedFields)
               && Subset(ResourceScopes, parent.ResourceScopes)
               && Subset(AllowedOperations, parent.AllowedOperations)
               && AtMost(MaxResults, parent.MaxResults)
               && AtMost(MaxAmount, parent.MaxAmount);

        static bool Subset(IReadOnlyList<string> child, IReadOnlyList<string> parent) =>
            parent.Count == 0
                ? true
                : child.Count > 0 && child.All(x => parent.Contains(x, StringComparer.Ordinal));

        static bool AtMost<T>(T? child, T? parent) where T : struct, IComparable<T> =>
            parent is null
                ? true
                : child is not null && child.Value.CompareTo(parent.Value) <= 0;
    }
}

/// <summary>
/// Cryptographically signed delegated authority. A warrant is evidence of
/// authority; current execution policy remains authoritative at runtime.
/// </summary>
public sealed record SecurityWarrant(
    string Id,
    string Issuer,
    string Subject,
    string Audience,
    IReadOnlyList<CapabilityGrant> Grants,
    SecurityWarrantConstraints Constraints,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Nonce,
    string KeyId,
    string? ParentId,
    byte[] Signature)
{
    /// <summary>Cryptographic digest of the immediate parent warrant, when delegated.</summary>
    public string? ParentDigest { get; init; }

    /// <summary>Ordered ancestor digests. The immediate parent is the last entry.</summary>
    public IReadOnlyList<string> DelegationPath { get; init; } = [];

    /// <summary>Number of delegation edges represented by this warrant.</summary>
    public int DelegationDepth => DelegationPath.Count;

    /// <summary>
    /// Cryptographic digest of the canonical unsigned form. Computed once per instance and then reused: the digest is
    /// read on every revocation, replay, cache-key and verification step of a request, and rebuilding the canonical
    /// JSON and re-hashing it each time is pure repeated CPU cost.
    /// </summary>
    /// <remarks>
    /// A warrant is an immutable value. Its collections must not be mutated after construction, or the cached digest
    /// would no longer match the canonical form. A <c>with</c> copy never reuses the original's digest, because the
    /// cache records which instance computed it (see <see cref="DigestMemo"/>).
    /// </remarks>
    public string Digest
    {
        get
        {
            var memo = Volatile.Read(ref _digestMemo);
            if (ReferenceEquals(memo.Owner, this)) return memo.Value!;
            var value = SecurityWarrantCanonicalizer.Digest(this);
            Volatile.Write(ref _digestMemo, new DigestMemo(this, value));
            return value;
        }
    }

    // Never null and always "equal", so this field cannot change record equality, hashing or ToString. A `with`
    // copy carries the original's memo reference, but the Owner check above makes that copy recompute its own digest.
    private DigestMemo _digestMemo = DigestMemo.Empty;

    private sealed class DigestMemo(SecurityWarrant? owner, string? value)
    {
        public static readonly DigestMemo Empty = new(null, null);
        public SecurityWarrant? Owner { get; } = owner;
        public string? Value { get; } = value;
        public override bool Equals(object? obj) => obj is DigestMemo;
        public override int GetHashCode() => 0;
    }

    public bool IsTimeValid(DateTimeOffset now) => now >= IssuedAt && now < ExpiresAt;
}
