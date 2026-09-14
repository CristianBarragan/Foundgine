using Foundgine.Core.Semantic.Planning;

namespace Foundgine.Core.Execution;

/// <summary>
/// Opaque physical plan produced by a provider compiler. The core semantic
/// model never depends on its contents.
/// </summary>
public abstract record ProviderPlan(string Provider)
{
    /// <summary>
    /// Security execution certificate. It is intentionally internal so callers
    /// cannot transplant or forge a certificate by assigning it directly.
    /// </summary>
    public SecurityInvariantProof? SecurityProof { get; internal set; }

    /// <summary>
    /// Provenance inherited from the authorized semantic plan. Provider plans
    /// must not be detached from the authorization decision that produced them.
    /// </summary>
    public SemanticPlanAuthorizationBinding? AuthorizationBinding { get; internal set; }

    internal void BindAuthorization(SemanticPlanAuthorizationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (AuthorizationBinding is not null && AuthorizationBinding != binding)
            throw new InvalidOperationException(
                "Provider plan authorization provenance cannot be replaced once established.");
        AuthorizationBinding = binding;
    }
}