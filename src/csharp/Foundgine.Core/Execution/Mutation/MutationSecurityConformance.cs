using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Concrete provider evidence for mutation execution. Provider declarations are
/// not sufficient to cross the mutation execution boundary.
/// </summary>
public sealed record MutationSecurityConformanceResult(
    string Provider,
    IReadOnlyList<string> Satisfied,
    IReadOnlyList<string> Violations)
{
    public void EnsureSatisfied(IReadOnlyCollection<string> required)
    {
        var missing = required
            .Except(Satisfied, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (Violations.Count == 0 && missing.Length == 0)
            return;

        var reasons = Violations
            .Concat(missing.Select(x => $"required invariant '{x}' was not satisfied"))
            .ToArray();

        throw new InvalidOperationException(
            $"Mutation provider '{Provider}' security conformance failed: {string.Join("; ", reasons)}");
    }
}

/// <summary>
/// Provider-specific evaluation of the actual mutation execution representation.
/// Implementations must report only guarantees they can establish for their
/// concrete execution path.
/// </summary>
public interface IMutationSecurityConformanceEvaluator
{
    MutationSecurityConformanceResult Evaluate(ExecutionMutationIR ir);
}

/// <summary>
/// In-process execution certificate bound to one exact mutation IR and one exact
/// provider instance. It is deliberately non-serializable/non-transferable.
/// </summary>
public sealed class MutationExecutionSecurityCertificate
{
    private readonly ExecutionMutationIR _boundIr;
    private readonly object _boundProvider;

    private MutationExecutionSecurityCertificate(
        ExecutionMutationIR boundIr,
        object boundProvider,
        string provider,
        IReadOnlyList<string> required,
        IReadOnlyList<string> preserved,
        IReadOnlyList<string> missing)
    {
        _boundIr = boundIr;
        _boundProvider = boundProvider;
        Provider = provider;
        Required = required;
        Preserved = preserved;
        Missing = missing;
        IrFingerprint = MutationExecutionIRFingerprint.Create(boundIr);
    }

    public string Provider { get; }
    public string IrFingerprint { get; }
    public IReadOnlyList<string> Required { get; }
    public IReadOnlyList<string> Preserved { get; }
    public IReadOnlyList<string> Missing { get; }
    public bool IsSatisfied => Missing.Count == 0;

    internal static MutationExecutionSecurityCertificate Create(
        ExecutionMutationIR ir,
        object provider,
        string providerName,
        IEnumerable<string> required,
        IEnumerable<string> preserved)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(preserved);

        var requiredSet = required.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var preservedSet = preserved.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var missing = requiredSet.Except(preservedSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return new MutationExecutionSecurityCertificate(
            ir, provider, providerName, requiredSet, preservedSet, missing);
    }

    internal bool IsBoundTo(ExecutionMutationIR ir, object provider) =>
        ReferenceEquals(_boundIr, ir) &&
        ReferenceEquals(_boundProvider, provider) &&
        string.Equals(IrFingerprint, MutationExecutionIRFingerprint.Create(ir), StringComparison.Ordinal);

    public void EnsureSatisfied()
    {
        if (!IsSatisfied)
            throw new InvalidOperationException(
                $"Mutation provider '{Provider}' cannot satisfy required security invariants: {string.Join(", ", Missing)}.");
    }
}

public static class MutationExecutionSecurityGate
{
    private static readonly IReadOnlySet<string> ProviderOwnedInvariants =
        new HashSet<string>(StringComparer.Ordinal)
        {
            SecurityInvariantIds.ParameterizedValues,
            SecurityInvariantIds.AtomicMutation,
            SecurityInvariantIds.MutationRowLocking
        };

    public static MutationExecutionSecurityCertificate Certify(
        ExecutionMutationIR ir,
        object provider,
        string providerName,
        IEnumerable<string> upstreamPreserved)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(upstreamPreserved);

        var required = ir.RequiredSecurityInvariants;
        if (required.Count == 0)
            throw new InvalidOperationException(
                "Mutation execution requires an explicit non-empty security invariant contract.");

        foreach (var id in required)
            if (!SecurityInvariantRegistry.Contains(id))
                throw new InvalidOperationException($"Unknown required security invariant '{id}'.");

        var preserved = new HashSet<string>(upstreamPreserved, StringComparer.Ordinal);
        var providerRequired = required.Where(ProviderOwnedInvariants.Contains).ToArray();

        if (providerRequired.Length > 0 && provider is not IMutationSecurityConformanceEvaluator evaluator)
            throw new InvalidOperationException(
                $"Mutation provider '{provider.GetType().Name}' has no concrete security conformance evaluator for: {string.Join(", ", providerRequired)}.");

        if (provider is IMutationSecurityConformanceEvaluator concrete)
        {
            var result = concrete.Evaluate(ir);
            if (!string.Equals(result.Provider, providerName, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Mutation provider conformance identity '{result.Provider}' does not match '{providerName}'.");

            result.EnsureSatisfied(providerRequired);
            foreach (var id in result.Satisfied)
                preserved.Add(id);
        }

        var certificate = MutationExecutionSecurityCertificate.Create(
            ir, provider, providerName, required, preserved);
        certificate.EnsureSatisfied();
        return certificate;
    }

    public static void EnsureExecutable(
        ExecutionMutationIR ir,
        object provider,
        MutationExecutionSecurityCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(certificate);

        if (!certificate.IsBoundTo(ir, provider))
            throw new InvalidOperationException(
                "Mutation execution certificate is not bound to the exact mutation IR and provider instance being executed.");

        certificate.EnsureSatisfied();
    }
}

internal static class MutationExecutionIRFingerprint
{
    public static string Create(ExecutionMutationIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        // Do not serialize MutationEntitySchema directly here. Its Fields member
        // is keyed by FieldId, and FieldId is intentionally a value object rather
        // than a JSON property-name type. Fingerprinting must be deterministic and
        // must not depend on System.Text.Json dictionary-key converter behavior.
        var operations = ir.Operations.Select(operation => new
        {
            entity = new
            {
                id = operation.Entity.Id.Value,
                name = operation.Entity.Name,
                columns = operation.Entity.Columns
                    .Select(column => column.Value)
                    .OrderBy(value => value)
                    .ToArray(),
                fields = operation.Entity.Fields
                    .OrderBy(pair => pair.Key.Value)
                    .Select(pair => new { field = pair.Key.Value, column = pair.Value?.Value })
                    .ToArray(),
                primaryKeyColumn = operation.Entity.PrimaryKeyColumn?.Value
            },
            kind = operation.Kind,
            fields = operation.Fields.Select(field => new
            {
                column = field.Column.Value,
                value = field.Value,
                source = field.Source is null
                    ? null
                    : new { field.Source.SourceOperationIndex, sourceField = field.Source.SourceField.Value }
            }).ToArray(),
            filter = operation.Filter,
            conflictColumns = operation.ConflictColumns?.Select(column => column.Value).ToArray(),
            returnFields = operation.ReturnFields?.Select(field => field.Value).ToArray()
        }).ToArray();

        var dependencies = ir.Dependencies
            .Select(dependency => new
            {
                dependency.SourceOperationIndex,
                dependency.TargetOperationIndex,
                sourceField = dependency.SourceField.Value,
                targetColumn = dependency.TargetColumn.Value
            })
            .ToArray();

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            version = "mutation-execution-ir-v1",
            operations,
            dependencies,
            requiredSecurityInvariants = ir.RequiredSecurityInvariants
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
        });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}