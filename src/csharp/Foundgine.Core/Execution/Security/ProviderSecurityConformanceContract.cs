namespace Foundgine.Core.Execution.Security;

/// <summary>
/// Executable provider conformance evidence for the concrete compiled plan.
/// Unlike a provider profile, this result is produced from the actual provider plan.
/// </summary>
public sealed record ProviderSecurityConformanceResult(
    string Provider,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Satisfied,
    IReadOnlyList<string> Violations)
{
    public bool IsSatisfied => Violations.Count == 0 && Required.All(Satisfied.Contains);

    public void EnsureSatisfied()
    {
        if (IsSatisfied)
            return;

        var missing = Required
            .Except(Satisfied, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var reasons = Violations
            .Concat(missing.Select(x => $"required invariant '{x}' was not satisfied"))
            .ToArray();

        throw new InvalidOperationException(
            $"Provider '{Provider}' security conformance failed: {string.Join("; ", reasons)}");
    }
}

/// <summary>
/// Provider-specific certification hook. Implementations inspect the actual
/// compiled ProviderPlan and must return concrete conformance evidence.
/// </summary>
public interface IProviderSecurityConformanceEvaluator
{
    ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan);
}
