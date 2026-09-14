using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Execution;

/// <summary>
/// Immutable security execution certificate bound to one exact provider plan
/// and one exact Execution IR fingerprint. Normal callers cannot construct or
/// attach this type; only the security certification gate can issue it.
/// </summary>
public sealed class SecurityInvariantProof
{
    private readonly ProviderPlan? _boundPlan;

    private SecurityInvariantProof(
        ProviderPlan? boundPlan,
        string executionIrFingerprint,
        string provider,
        IReadOnlyList<string> required,
        IReadOnlyList<string> preserved,
        IReadOnlyList<string> missing)
    {
        _boundPlan = boundPlan;
        ExecutionIrFingerprint = executionIrFingerprint;
        Provider = provider;
        Required = required;
        Preserved = preserved;
        Missing = missing;
    }

    public string Provider { get; }
    public IReadOnlyList<string> Required { get; }
    public IReadOnlyList<string> Preserved { get; }
    public IReadOnlyList<string> Missing { get; }
    public string ExecutionIrFingerprint { get; }
    public bool IsSatisfied => Missing.Count == 0;

    internal bool IsBoundTo(ProviderPlan plan, ExecutionIR ir) =>
        _boundPlan is not null &&
        ReferenceEquals(_boundPlan, plan) &&
        string.Equals(ExecutionIrFingerprint, ExecutionIRFingerprint.Create(ir), StringComparison.Ordinal);

    public void EnsureSatisfied()
    {
        if (!IsSatisfied)
            throw new InvalidOperationException(
                $"Provider '{Provider}' cannot satisfy required security invariants: {string.Join(", ", Missing)}.");
    }

    // Test/diagnostic-only unbound evidence. It can never cross the execution
    // boundary because IsBoundTo deliberately rejects certificates without an
    // exact plan + IR binding. It is internal so application callers cannot
    // manufacture executable security certificates.
    internal static SecurityInvariantProof Create(
        string provider,
        IEnumerable<string> required,
        IEnumerable<string> preserved)
    {
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(preserved);
        var requiredSet = required.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var preservedSet = preserved.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var missing = requiredSet.Except(preservedSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        return new SecurityInvariantProof(null, string.Empty, provider, requiredSet, preservedSet, missing);
    }

    internal static SecurityInvariantProof Create(
        ProviderPlan plan,
        ExecutionIR ir,
        IEnumerable<string> required,
        IEnumerable<string> preserved)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(preserved);

        var requiredSet = required
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var preservedSet = preserved
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var missing = requiredSet
            .Except(preservedSet, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return new SecurityInvariantProof(
            plan,
            ExecutionIRFingerprint.Create(ir),
            plan.Provider,
            requiredSet,
            preservedSet,
            missing);
    }
}

/// <summary>
/// Provider declaration of capabilities. A declaration is not sufficient
/// evidence for security-critical provider invariants; those require a
/// concrete evaluator over the compiled provider plan.
/// </summary>
public interface ISecurityInvariantProviderCompiler
{
    IReadOnlyCollection<string> PreservedSecurityInvariants { get; }
}

public static class SecurityInvariantProofGate
{
    private static readonly IReadOnlySet<string> ConcreteEvaluationRequired =
        new HashSet<string>(StringComparer.Ordinal)
        {
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.RuntimeAuthorization,
            SecurityInvariantIds.TenantIsolation,
            SecurityInvariantIds.FieldVisibility,
            SecurityInvariantIds.RelationshipVisibility,
            SecurityInvariantIds.ParameterizedValues,
            SecurityInvariantIds.PlanCacheContextIsolation,
            SecurityInvariantIds.AtomicMutation,
            SecurityInvariantIds.MutationRowLocking,
            SecurityInvariantIds.Idempotency,
            SecurityInvariantIds.ReplayProtection,
            SecurityInvariantIds.AuditRequired,
            SecurityInvariantIds.ExecutionEvidenceRequired
        };

    public static ProviderPlan AttachAndValidate(
        ProviderPlan plan,
        ExecutionIR ir,
        IProviderPlanCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(compiler);

        var required = ir.RequiredSecurityInvariants;
        if (required.Count == 0)
            throw new InvalidOperationException(
                "ExecutionIR contains no security obligations. An executable provider plan must carry a non-empty security certificate.");

        if (compiler is not ISecurityInvariantProviderCompiler securityCompiler)
        {
            throw new InvalidOperationException(
                $"Provider compiler '{compiler.GetType().Name}' does not declare a security-invariant preservation contract.");
        }

        foreach (var id in required)
        {
            if (!SecurityInvariantRegistry.Contains(id))
                throw new InvalidOperationException($"Unknown required security invariant '{id}'.");
        }

        var requiredConcreteEvaluation = required
            .Where(ConcreteEvaluationRequired.Contains)
            .ToArray();

        if (requiredConcreteEvaluation.Length > 0 && compiler is not IProviderSecurityConformanceEvaluator)
        {
            throw new InvalidOperationException(
                $"Provider compiler '{compiler.GetType().Name}' has no concrete security conformance evaluator for security-critical invariants: {string.Join(", ", requiredConcreteEvaluation)}.");
        }

        IReadOnlyCollection<string> preserved = securityCompiler.PreservedSecurityInvariants;

        // The provider profile is a capability declaration only. When an
        // executable evaluator exists, its concrete result is the authority
        // used to issue the execution certificate.
        if (compiler is IProviderSecurityConformanceEvaluator evaluator)
        {
            var conformance = evaluator.Evaluate(ir, plan);
            conformance.EnsureSatisfied();

            var missing = required
                .Except(conformance.Satisfied, StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException(
                    $"Provider '{plan.Provider}' executable conformance did not satisfy required invariants: {string.Join(", ", missing)}.");

            preserved = conformance.Satisfied;
        }
        else
        {
            var missing = required
                .Except(preserved, StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException(
                    $"Provider '{plan.Provider}' declared preservation does not satisfy required invariants: {string.Join(", ", missing)}.");
        }

        // Bind the certificate to the exact returned ProviderPlan object. Do not
        // use `with { SecurityProof = proof }` after issuing the certificate: a
        // record clone would detach the certificate from the object it certifies.
        var certifiedPlan = plan with { SecurityProof = null };
        var proof = SecurityInvariantProof.Create(certifiedPlan, ir, required, preserved);
        proof.EnsureSatisfied();
        certifiedPlan.SecurityProof = proof;
        return certifiedPlan;
    }
}

internal static class ExecutionIRFingerprint
{
    public static string Create(ExecutionIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var json = System.Text.Json.JsonSerializer.Serialize(ir);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}