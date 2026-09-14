namespace Foundgine.Core.Semantic;

/// <summary>
/// Stable semantic version identifiers carried across discovery, planning,
/// approval and execution. The semantic model version is derived from the
/// canonical model topology so a changed model cannot silently reuse an old
/// approval.
/// </summary>
public sealed record SemanticVersionSet(
    string SemanticModelVersion,
    int CapabilityContractVersion,
    int CapabilityVersion,
    int IntentVersion,
    int PlanVersion)
{
    public const int CurrentCapabilityContractVersion = 1;
    public const int CurrentCapabilityVersion = 1;
    public const int CurrentIntentVersion = 1;
    public const int CurrentPlanVersion = 1;

    public static SemanticVersionSet For(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new SemanticVersionSet(
            ComputeModelVersion(model),
            CurrentCapabilityContractVersion,
            CurrentCapabilityVersion,
            CurrentIntentVersion,
            CurrentPlanVersion);
    }

    private static string ComputeModelVersion(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return $"sha256:{model.ContractFingerprint}";
    }
}