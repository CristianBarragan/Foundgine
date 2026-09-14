using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Intent;

/// <summary>
/// A serializable, provider-neutral envelope for dynamic semantic intent.
/// The contract fingerprint binds the request to the semantic contract against
/// which the caller constructed it. The intent itself remains unresolved until
/// a trusted runtime resolves it against the frozen contract snapshot.
/// </summary>
public sealed record SemanticIntentDocument(
    string ContractFingerprint,
    ReadIntent Intent,
    int Version = 1)
{
    public const int CurrentVersion = 1;

    public SemanticIntentDocument Validate()
    {
        if (Version != CurrentVersion)
            throw new InvalidOperationException($"Unsupported semantic intent document version '{Version}'.");

        if (string.IsNullOrWhiteSpace(ContractFingerprint))
            throw new InvalidOperationException("Semantic intent document requires a contract fingerprint.");

        ArgumentNullException.ThrowIfNull(Intent);
        return this;
    }
}

/// <summary>
/// Explicit result of resolving an intent document. The document remains
/// available for evidence/auditing while the resolved request is canonical IR.
/// </summary>
public sealed record SemanticIntentResolution(
    SemanticIntentDocument Document,
    SemanticRequest Request,
    string ContractFingerprint);
