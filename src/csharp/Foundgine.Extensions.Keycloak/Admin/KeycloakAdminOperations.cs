using System.Text.Json;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Temporal;
using Foundgine.Extensions.Temporal.Activities;
using Foundgine.Extensions.Temporal.Contracts;
using Foundgine.Extensions.Temporal.Hosting;

namespace Foundgine.Extensions.Keycloak.Admin;

/// <summary>
/// Customer-onboarding operations backed by the Keycloak Admin API, for use as Temporal process steps.
/// Safety rules shared by all of them:
/// <list type="bullet">
/// <item>The tenant always comes from the workflow (<see cref="FoundgineOperationContext.TenantId"/>), never from the payload.</item>
/// <item>Created users and groups carry the creating workflow's id; update/delete only touches objects of the same
/// tenant that this same workflow created, so a payload can neither reach another tenant nor remove pre-existing accounts.</item>
/// <item>Roles and required actions in a payload must be on the configured allow-lists. No passwords are handled:
/// new users set their own through the UPDATE_PASSWORD required action.</item>
/// <item>Each operation is idempotent across Temporal retries and compensations never fail the saga for missing objects.</item>
/// </list>
/// </summary>
public abstract class KeycloakAdminOperation(string name, KeycloakAdminClient admin) : IFoundgineTemporalOperation
{
    protected KeycloakAdminClient Admin { get; } = admin;
    public string Name { get; } = name;
    public abstract Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext context, CancellationToken cancellationToken);

    protected static string Tenant(FoundgineOperationContext c) =>
        KeycloakAdminClient.IsValidTenant(c.TenantId) ? c.TenantId
            : throw new FoundgineNonRetryableOperationException("Tenant id is not valid for Keycloak (allowed: letters, digits, '.', '_', '-'; max 64).");

    /// <summary>The workflow id: the idempotency key is "{workflowId}:{activityId}" and is stable across retries.</summary>
    protected static string Workflow(FoundgineOperationContext c)
    {
        var i = c.IdempotencyKey.LastIndexOf(':');
        return i > 0 ? c.IdempotencyKey[..i] : throw new FoundgineNonRetryableOperationException("Operation has no workflow identity.");
    }

    protected static JsonElement Payload(FoundgineOperationContext c)
    {
        try { return JsonDocument.Parse(c.PayloadJson).RootElement.Clone(); }
        catch (JsonException) { throw new FoundgineNonRetryableOperationException("Payload is not valid JSON."); }
    }

    protected static string? Str(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    protected static List<string> Strings(JsonElement p, string name)
    {
        var list = new List<string>();
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty(name, out var v)) return list;
        if (v.ValueKind != JsonValueKind.Array) throw new FoundgineNonRetryableOperationException($"'{name}' must be an array of strings.");
        foreach (var e in v.EnumerateArray())
            list.Add(e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s ? s
                : throw new FoundgineNonRetryableOperationException($"'{name}' must contain only non-empty strings."));
        return list.Distinct(StringComparer.Ordinal).ToList();
    }

    protected static string RequireUsername(JsonElement p) =>
        Str(p, "username") is { } u && KeycloakAdminClient.IsValidUsername(u) ? u
            : throw new FoundgineNonRetryableOperationException("'username' is required (letters, digits, '.', '_', '@', '+', '-'; max 128).");

    protected static FoundgineOperationResult Id(string key, string id) =>
        new(JsonSerializer.Serialize(new Dictionary<string, string> { [key] = id }));

    protected static bool OwnedBy(string? tenantAttr, string? provenance, string tenant, string workflow) =>
        string.Equals(tenantAttr, tenant, StringComparison.Ordinal) && string.Equals(provenance, workflow, StringComparison.Ordinal);
}

/// <summary>Creates the top-level group for the workflow's tenant (named after the tenant id). Output: <c>{"groupId":"..."}</c>.</summary>
public sealed class CreateTenantGroup(KeycloakAdminClient admin) : KeycloakAdminOperation("keycloak.create-tenant-group", admin)
{
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var tenant = Tenant(c);
        var workflow = Workflow(c);

        var existing = await Admin.FindGroupAsync(tenant, ct).ConfigureAwait(false);
        if (existing is null)
        {
            var id = await Admin.CreateGroupAsync(tenant, tenant, workflow, ct).ConfigureAwait(false);
            if (id is not null) return Id("groupId", id);
            existing = await Admin.FindGroupAsync(tenant, ct).ConfigureAwait(false); // lost a creation race
        }
        return existing is not null && OwnedBy(existing.TenantId, existing.ProvisionedBy, tenant, workflow)
            ? Id("groupId", existing.Id) // a retry of this very step
            : throw new FoundgineNonRetryableOperationException("A Keycloak group for this tenant already exists and was not created by this process.");
    }
}

/// <summary>Compensation for <see cref="CreateTenantGroup"/>: removes the group only if this workflow created it.</summary>
public sealed class DeleteTenantGroup(KeycloakAdminClient admin) : KeycloakAdminOperation("keycloak.delete-tenant-group", admin)
{
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var tenant = Tenant(c);
        var group = await Admin.FindGroupAsync(tenant, ct).ConfigureAwait(false);
        if (group is not null && OwnedBy(group.TenantId, group.ProvisionedBy, tenant, Workflow(c)))
            await Admin.DeleteGroupAsync(group.Id, ct).ConfigureAwait(false);
        return new FoundgineOperationResult();
    }
}

/// <summary>
/// Creates a user in the workflow's tenant. Payload: <c>username</c> (required), <c>email</c>, <c>firstName</c>, <c>lastName</c>,
/// <c>roles</c> (subset of <see cref="FoundgineKeycloakOptions.AdminAssignableRoles"/>), <c>requiredActions</c>
/// (subset of <see cref="FoundgineKeycloakOptions.AdminAllowedRequiredActions"/>). Joins the tenant group when one exists.
/// Output: <c>{"userId":"..."}</c>.
/// </summary>
public sealed class CreateUser(KeycloakAdminClient admin, FoundgineKeycloakOptions options)
    : KeycloakAdminOperation("keycloak.create-user", admin)
{
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var tenant = Tenant(c);
        var workflow = Workflow(c);
        var p = Payload(c);

        var username = RequireUsername(p);
        var email = Str(p, "email");
        if (email is not null && (email.Length > 254 || !email.Contains('@'))) throw new FoundgineNonRetryableOperationException("'email' is not valid.");

        var roles = Strings(p, "roles");
        var actions = Strings(p, "requiredActions");
        var badRole = roles.FirstOrDefault(r => !options.AdminAssignableRoles.Contains(r, StringComparer.Ordinal));
        if (badRole is not null) throw new FoundgineNonRetryableOperationException($"Role '{badRole}' is not assignable by onboarding.");
        var badAction = actions.FirstOrDefault(a => !options.AdminAllowedRequiredActions.Contains(a, StringComparer.Ordinal));
        if (badAction is not null) throw new FoundgineNonRetryableOperationException($"Required action '{badAction}' is not allowed.");

        var userId = await EnsureUserAsync(
            new KeycloakNewUser(username, email, Str(p, "firstName"), Str(p, "lastName"), actions), tenant, workflow, ct).ConfigureAwait(false);

        await Admin.AssignRealmRolesAsync(userId, roles, ct).ConfigureAwait(false);

        var group = await Admin.FindGroupAsync(tenant, ct).ConfigureAwait(false);
        if (group is not null && string.Equals(group.TenantId, tenant, StringComparison.Ordinal))
            await Admin.AddUserToGroupAsync(userId, group.Id, ct).ConfigureAwait(false);

        return Id("userId", userId);
    }

    private async Task<string> EnsureUserAsync(KeycloakNewUser user, string tenant, string workflow, CancellationToken ct)
    {
        // Same message for "taken by someone else" however it was detected: it must not reveal other tenants' users.
        var taken = new FoundgineNonRetryableOperationException("That username is not available.");

        var existing = await Admin.FindUserAsync(user.Username, ct).ConfigureAwait(false);
        if (existing is null)
        {
            var id = await Admin.CreateUserAsync(user, tenant, workflow, ct).ConfigureAwait(false);
            if (id is not null) return id;
            existing = await Admin.FindUserAsync(user.Username, ct).ConfigureAwait(false); // lost a creation race
        }
        return existing is not null && OwnedBy(existing.TenantId, existing.ProvisionedBy, tenant, workflow) ? existing.Id : throw taken;
    }
}

/// <summary>Compensation for <see cref="CreateUser"/>: payload <c>username</c>; deletes only a user this workflow created in this tenant.</summary>
public sealed class DeleteUser(KeycloakAdminClient admin) : KeycloakAdminOperation("keycloak.delete-user", admin)
{
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var tenant = Tenant(c);
        var user = await Admin.FindUserAsync(RequireUsername(Payload(c)), ct).ConfigureAwait(false);
        if (user is not null && OwnedBy(user.TenantId, user.ProvisionedBy, tenant, Workflow(c)))
            await Admin.DeleteUserAsync(user.Id, ct).ConfigureAwait(false);
        return new FoundgineOperationResult();
    }
}

/// <summary>Offboarding: payload <c>username</c>; disables any user of the workflow's tenant. Other tenants' users look like "not found".</summary>
public sealed class DisableUser(KeycloakAdminClient admin) : KeycloakAdminOperation("keycloak.disable-user", admin)
{
    public override async Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var tenant = Tenant(c);
        var user = await Admin.FindUserAsync(RequireUsername(Payload(c)), ct).ConfigureAwait(false);
        if (user is null || !string.Equals(user.TenantId, tenant, StringComparison.Ordinal))
            throw new FoundgineNonRetryableOperationException("User not found in this tenant.");
        if (user.Enabled) await Admin.SetUserEnabledAsync(user.Id, false, ct).ConfigureAwait(false);
        return Id("userId", user.Id);
    }
}

public static class FoundgineKeycloakAdminExtensions
{
    /// <summary>
    /// Registers the Keycloak onboarding operations on a Temporal worker (requires <c>AddFoundgineKeycloakWarrants</c> on the
    /// same host, plus <c>ServiceClientId</c>/<c>ServiceClientSecret</c> for a service account with user-management roles).
    /// </summary>
    public static FoundgineTemporalBuilder AddKeycloakAdminOperations(this FoundgineTemporalBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddOperation<CreateTenantGroup>()
            .AddOperation<DeleteTenantGroup>()
            .AddOperation<CreateUser>()
            .AddOperation<DeleteUser>()
            .AddOperation<DisableUser>();
    }
}
