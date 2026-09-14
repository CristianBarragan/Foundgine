namespace Foundgine.Core.Semantic.Security;

/// <summary>
/// Stable machine-readable security invariant attached to a semantic capability
/// or execution plan. Invariants describe required guarantees; they do not
/// grant authorization and never replace execution-time policy evaluation.
/// </summary>
public sealed record SecurityInvariant(
    string Id,
    string Name,
    string Description,
    SecurityInvariantPhase Phase,
    bool MustBePreservedByProvider);

public enum SecurityInvariantPhase
{
    IntentBoundary = 0,
    SemanticResolution = 1,
    Planning = 2,
    ProviderCompilation = 3,
    Execution = 4,
    Evidence = 5
}

/// <summary>Canonical identifiers for Foundgine security invariants.</summary>
public static class SecurityInvariantIds
{
    public const string AuthorizationRequired = "authorization.required";
    public const string RuntimeAuthorization = "authorization.runtime";
    public const string AuthorizationOwnership = "authorization.ownership";
    public const string TenantIsolation = "tenant.isolation";
    public const string FieldVisibility = "visibility.field";
    public const string RelationshipVisibility = "visibility.relationship";
    public const string ParameterizedValues = "execution.parameterized-values";
    public const string PlanCacheContextIsolation = "planning.cache-context-isolation";
    public const string AtomicMutation = "mutation.atomic";
    public const string MutationRowLocking = "mutation.atomic.row-locking";
    public const string Idempotency = "mutation.idempotency";
    public const string ReplayProtection = "mutation.replay-protection";
    public const string AuditRequired = "evidence.audit";
    public const string ExecutionEvidenceRequired = "evidence.execution-receipt";
    public const string TransactionReadCommittedIsolation = "mutation.transaction.read-committed-isolation";
    public const string MutationDailyLimit = "mutation.daily-limit";
}

/// <summary>
/// Canonical registry. The registry is deliberately provider-neutral so every
/// adapter, planner and provider can reason about the same invariant vocabulary.
/// </summary>
public static class SecurityInvariantRegistry
{
    private static readonly IReadOnlyDictionary<string, SecurityInvariant> All =
        new Dictionary<string, SecurityInvariant>(StringComparer.Ordinal)
        {
            [SecurityInvariantIds.AuthorizationRequired] = new(
                SecurityInvariantIds.AuthorizationRequired, "Authorization required",
                "The capability may execute only when its effective authorization policy permits the operation.",
                SecurityInvariantPhase.SemanticResolution, true),
            [SecurityInvariantIds.RuntimeAuthorization] = new(
                SecurityInvariantIds.RuntimeAuthorization, "Runtime authorization",
                "Authorization must be evaluated against current execution context rather than trusted model-supplied authority.",
                SecurityInvariantPhase.Execution, true),
            [SecurityInvariantIds.AuthorizationOwnership] = new(
                SecurityInvariantIds.AuthorizationOwnership, "Authorization ownership",
                "A consequential account mutation must not operate on accounts the acting principal does not own, even if an upstream authorization dependency incorrectly permits the request.",
                SecurityInvariantPhase.Execution, true),
            [SecurityInvariantIds.TenantIsolation] = new(
                SecurityInvariantIds.TenantIsolation, "Tenant isolation",
                "Data and mutations must remain within the effective tenant boundary.",
                SecurityInvariantPhase.Execution, true),
            [SecurityInvariantIds.FieldVisibility] = new(
                SecurityInvariantIds.FieldVisibility, "Field visibility",
                "Fields not exposed by the effective semantic policy must not become selectable or writable.",
                SecurityInvariantPhase.SemanticResolution, true),
            [SecurityInvariantIds.RelationshipVisibility] = new(
                SecurityInvariantIds.RelationshipVisibility, "Relationship visibility",
                "Relationships not exposed by the effective semantic policy must not become traversable.",
                SecurityInvariantPhase.SemanticResolution, true),
            [SecurityInvariantIds.ParameterizedValues] = new(
                SecurityInvariantIds.ParameterizedValues, "Parameterized values",
                "Untrusted values must remain data parameters and must not become executable provider syntax.",
                SecurityInvariantPhase.ProviderCompilation, true),
            [SecurityInvariantIds.PlanCacheContextIsolation] = new(
                SecurityInvariantIds.PlanCacheContextIsolation, "Plan cache context isolation",
                "Reusable provider plans must not freeze request-specific authority or tenant values.",
                SecurityInvariantPhase.Planning, true),
            [SecurityInvariantIds.AtomicMutation] = new(
                SecurityInvariantIds.AtomicMutation, "Atomic mutation",
                "A capability requiring atomic mutation must preserve its state transition as one transactionally consistent operation.",
                SecurityInvariantPhase.Execution, true),
            [SecurityInvariantIds.MutationRowLocking] = new(
                SecurityInvariantIds.MutationRowLocking, "Mutation row locking",
                "Protected mutations must lock all affected state deterministically before applying the state transition.",
                SecurityInvariantPhase.Execution, true),
            [SecurityInvariantIds.Idempotency] = new(
                SecurityInvariantIds.Idempotency, "Idempotency",
                "Repeated requests carrying the same semantic idempotency identity must not repeat the protected side effect.",
                SecurityInvariantPhase.Execution, true),
            [SecurityInvariantIds.ReplayProtection] = new(
                SecurityInvariantIds.ReplayProtection, "Replay protection",
                "An idempotency identity cannot be rebound to materially different actor, tenant, target or value context.",
                SecurityInvariantPhase.Execution, true),
            [SecurityInvariantIds.AuditRequired] = new(
                SecurityInvariantIds.AuditRequired, "Audit required",
                "Protected mutations must emit the required audit evidence as part of successful execution.",
                SecurityInvariantPhase.Evidence, true),
            [SecurityInvariantIds.ExecutionEvidenceRequired] = new(
                SecurityInvariantIds.ExecutionEvidenceRequired, "Execution evidence required",
                "Protected execution must produce an evidence-bearing execution receipt.",
                SecurityInvariantPhase.Evidence, true),
            [SecurityInvariantIds.MutationDailyLimit] = new(
                SecurityInvariantIds.MutationDailyLimit, "Mutation daily limit",
                "A consequential transfer must not increase an account's daily transferred amount beyond its configured daily limit, including when multiple transfers share one batch.",
                SecurityInvariantPhase.Execution, true),
            [SecurityInvariantIds.TransactionReadCommittedIsolation] = new(
                SecurityInvariantIds.TransactionReadCommittedIsolation, "Transaction read-committed isolation",
                "A protected mutation's transaction must run under an explicit read-committed (or stronger) isolation level rather than an unspecified default.",
                SecurityInvariantPhase.Execution, true)
        };

    public static IReadOnlyCollection<SecurityInvariant> AllInvariants => All.Values.ToList().AsReadOnly();

    public static SecurityInvariant Get(string id) =>
        All.TryGetValue(id, out var invariant)
            ? invariant
            : throw new KeyNotFoundException($"Unknown security invariant '{id}'.");

    public static bool Contains(string id) => All.ContainsKey(id);

    public static SecurityInvariantSet CreateSet(IEnumerable<string> ids) =>
        new(ids.Distinct(StringComparer.Ordinal).Select(Get).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
}

/// <summary>Immutable, deterministically ordered invariant requirements.</summary>
public sealed record SecurityInvariantSet(IReadOnlyList<SecurityInvariant> Invariants)
{
    public bool Contains(string id) => Invariants.Any(x => x.Id == id);

    public void Require(string id)
    {
        if (!Contains(id))
            throw new InvalidOperationException($"Required security invariant '{id}' is missing.");
    }
}