using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Execution.Security;

/// <summary>
/// Provider-neutral security conformance profile. A profile describes what a
/// provider can preserve; it does not grant the provider authority to execute a
/// capability. The plan still supplies the required invariant set.
/// </summary>
public sealed record ProviderSecurityConformanceProfile(
    string Provider,
    IReadOnlyCollection<string> PreservedSecurityInvariants,
    IReadOnlyCollection<string> Notes)
{
    public SecurityInvariantAttestation Evaluate(IEnumerable<string> requiredInvariants)
    {
        ArgumentNullException.ThrowIfNull(requiredInvariants);

        foreach (var invariant in requiredInvariants)
        {
            if (!SecurityInvariantRegistry.Contains(invariant))
                throw new InvalidOperationException($"Unknown required security invariant '{invariant}'.");
        }

        return SecurityInvariantAttestation.Create(
            Provider,
            requiredInvariants,
            PreservedSecurityInvariants);
    }
}

/// <summary>
/// Provider-neutral conformance matrix. It makes provider capability differences
/// explicit instead of silently treating every provider as equivalent.
/// </summary>
public sealed class ProviderSecurityConformanceMatrix
{
    private readonly Dictionary<string, ProviderSecurityConformanceProfile> _profiles =
        new(StringComparer.Ordinal);

    public ProviderSecurityConformanceMatrix Register(ProviderSecurityConformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Provider))
            throw new ArgumentException("Provider name is required.", nameof(profile));

        foreach (var invariant in profile.PreservedSecurityInvariants)
        {
            if (!SecurityInvariantRegistry.Contains(invariant))
                throw new InvalidOperationException(
                    $"Provider '{profile.Provider}' declares unknown security invariant '{invariant}'.");
        }

        _profiles[profile.Provider] = profile;
        return this;
    }

    public IReadOnlyCollection<ProviderSecurityConformanceProfile> Profiles => _profiles.Values.ToArray();

    public ProviderSecurityConformanceProfile Get(string provider) =>
        _profiles.TryGetValue(provider, out var profile)
            ? profile
            : throw new KeyNotFoundException(
                $"Provider '{provider}' is not registered in the security conformance matrix.");

    public SecurityInvariantAttestation Evaluate(string provider, IEnumerable<string> requiredInvariants) =>
        Get(provider).Evaluate(requiredInvariants);

    public SecurityInvariantAttestation EnsureSatisfied(string provider, IEnumerable<string> requiredInvariants)
    {
        var proof = Evaluate(provider, requiredInvariants);
        proof.EnsureSatisfied();
        return proof;
    }
}

/// <summary>Canonical baseline profiles for providers shipped with Foundgine.</summary>
public static class FoundgineProviderSecurityProfiles
{
    public static ProviderSecurityConformanceProfile InMemory => new(
        "in-memory",
        [
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.RuntimeAuthorization,
            SecurityInvariantIds.TenantIsolation,
            SecurityInvariantIds.FieldVisibility,
            SecurityInvariantIds.RelationshipVisibility,
            SecurityInvariantIds.ParameterizedValues,
            SecurityInvariantIds.PlanCacheContextIsolation
        ],
        [
            "Suitable for semantic/query security testing; consequential PostgreSQL transaction guarantees are not inferred."
        ]);

    public static ProviderSecurityConformanceProfile Sql => new(
        "sql",
        [
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.RuntimeAuthorization,
            SecurityInvariantIds.FieldVisibility,
            SecurityInvariantIds.RelationshipVisibility,
            SecurityInvariantIds.ParameterizedValues,
            SecurityInvariantIds.PlanCacheContextIsolation
        ],
        [
            "Generic SQL preserves query-level guarantees; mutation guarantees require a provider-specific execution contract."
        ]);

    public static ProviderSecurityConformanceProfile PostgresTransferFunds => new(
        "postgres-transfer-funds",
        [
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.RuntimeAuthorization,
            SecurityInvariantIds.TenantIsolation,
            SecurityInvariantIds.ParameterizedValues,
            SecurityInvariantIds.PlanCacheContextIsolation,
            SecurityInvariantIds.AtomicMutation,
            SecurityInvariantIds.Idempotency,
            SecurityInvariantIds.ReplayProtection,
            SecurityInvariantIds.AuditRequired,
            SecurityInvariantIds.ExecutionEvidenceRequired
        ],
        [
            "High-assurance TransferFunds provider; transaction and concurrency guarantees are backed by PostgreSQL integration tests."
        ]);
}