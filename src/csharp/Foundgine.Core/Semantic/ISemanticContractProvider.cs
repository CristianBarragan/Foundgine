namespace Foundgine.Core.Semantic;

/// <summary>
/// Supplies the trusted immutable semantic contract used by runtime components.
/// Construction and configuration APIs are deliberately absent from this port.
/// </summary>
public interface ISemanticContractProvider
{
    SemanticContractSnapshot Contract { get; }
}

/// <summary>Default singleton provider for a frozen semantic contract snapshot.</summary>
public sealed class SemanticContractProvider : ISemanticContractProvider
{
    public SemanticContractProvider(SemanticContractSnapshot contract)
    {
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
    }

    public SemanticContractSnapshot Contract { get; }
}