using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Keycloak.Warrants;
using Foundgine.Extensions.Temporal.Security;

namespace Foundgine.Extensions.Keycloak.Security;

/// <summary>
/// Temporal worker authority. Activities run as the initiating subject (for audit) but with the grants of
/// <see cref="FoundgineKeycloakOptions.WorkerRoles"/>, bounded to the workflow's tenant, and short-lived.
/// Authority is therefore the worker's, not a replay of the user's token (which has long expired and must not be
/// stored in workflow history). Keep WorkerRoles least-privilege.
/// </summary>
public sealed class KeycloakTemporalSecurityContextFactory(
    IFoundgineWarrantIssuer issuer, FoundgineKeycloakOptions options) : IFoundgineTemporalSecurityContextFactory
{
    public ValueTask<SecurityExecutionContext> CreateAsync(
        string tenantId, string initiator, CancellationToken cancellationToken = default)
    {
        var context = issuer.Issue(initiator, tenantId, options.WorkerRoles, options.WorkerWarrantLifetime)
            ?? throw new UnauthorizedAccessException("WorkerRoles grant no Foundgine capabilities for this tenant.");
        return ValueTask.FromResult(context);
    }
}
