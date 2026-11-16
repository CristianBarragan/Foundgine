using Foundgine.Extensions.Temporal;

/// <summary>
/// Customer onboarding: create the tenant's Keycloak group, then its first admin user, undoing both if a later step fails.
/// Start with payload { "username": "...", "email": "...", "firstName": "...", "lastName": "...",
/// "roles": ["foundgine-user"], "requiredActions": ["UPDATE_PASSWORD", "VERIFY_EMAIL"] }.
/// The tenant comes from the caller's authenticated context, not from the payload.
/// </summary>
public static class OnboardingProcess
{
    public const string Name = "customer-onboarding";

    public static FoundgineProcessDefinition Definition { get; } = new(Name,
    [
        new("group", "keycloak.create-tenant-group", CompensationOperation: "keycloak.delete-tenant-group"),
        new("admin-user", "keycloak.create-user", CompensationOperation: "keycloak.delete-user")
    ]);
}
