namespace Foundgine.Core.Serialization;

/// <summary>
/// Bounds applied while parsing untrusted structured intent. These limits are
/// intentionally enforced at the protocol boundary before semantic resolution.
/// </summary>
public sealed record JsonReadIntentAdapterOptions
{
    public int MaxSelectionDepth { get; init; } = 32;
    public int MaxSelections { get; init; } = 256;
    public int MaxFilterDepth { get; init; } = 32;
    public int MaxFilterNodes { get; init; } = 256;
    public int MaxJsonValueDepth { get; init; } = 16;

    /// <summary>Rejects model-controlled properties that are outside the canonical intent contract.</summary>
    public bool RejectUnknownProperties { get; init; } = true;
}