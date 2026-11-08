package com.foundgine.extensions.keycloak.admin;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.extensions.keycloak.configuration.FoundgineKeycloakOptions;
import com.foundgine.extensions.temporal.activities.FoundgineNonRetryableOperationException;
import com.foundgine.extensions.temporal.activities.FoundgineOperationContext;
import com.foundgine.extensions.temporal.activities.IFoundgineTemporalOperation;
import com.foundgine.extensions.temporal.contracts.FoundgineOperationResult;
import com.foundgine.providers.json.FoundgineJson;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;

/**
 * Customer-onboarding operations backed by the Keycloak Admin API, for use as Temporal process steps. Safety rules shared
 * by all of them:
 * <ul>
 * <li>The tenant always comes from the workflow ({@link FoundgineOperationContext#tenantId()}), never from the payload.</li>
 * <li>Created users and groups carry the creating workflow's id; update/delete only touches objects of the same tenant that
 * this same workflow created, so a payload can neither reach another tenant nor remove pre-existing accounts.</li>
 * <li>Roles and required actions in a payload must be on the configured allow-lists. No passwords are handled: new users set
 * their own through the UPDATE_PASSWORD required action.</li>
 * <li>Each operation is idempotent across retries and compensations never fail the saga for missing objects.</li>
 * </ul>
 */
public final class KeycloakAdminOperations {
    private KeycloakAdminOperations() { }

    /**
     * Java equivalent of {@code AddKeycloakAdminOperations}: the five operations, ready to hand to
     * {@code FoundgineOperationActivities}.
     */
    public static List<IFoundgineTemporalOperation> create(KeycloakAdminClient admin, FoundgineKeycloakOptions options) {
        return List.of(new CreateTenantGroup(admin), new DeleteTenantGroup(admin), new CreateUser(admin, options),
                new DeleteUser(admin), new DisableUser(admin));
    }

    public abstract static class KeycloakAdminOperation implements IFoundgineTemporalOperation {
        protected final KeycloakAdminClient admin;
        private final String name;

        protected KeycloakAdminOperation(String name, KeycloakAdminClient admin) {
            this.name = name;
            this.admin = admin;
        }

        @Override public String name() { return name; }

        protected static String tenant(FoundgineOperationContext c) {
            if (!KeycloakAdminClient.isValidTenant(c.tenantId()))
                throw new FoundgineNonRetryableOperationException(
                        "Tenant id is not valid for Keycloak (allowed: letters, digits, '.', '_', '-'; max 64).");
            return c.tenantId();
        }

        /** The workflow id: the idempotency key is {@code {workflowId}:{activityId}} and is stable across retries. */
        protected static String workflow(FoundgineOperationContext c) {
            int i = c.idempotencyKey() == null ? -1 : c.idempotencyKey().lastIndexOf(':');
            if (i <= 0) throw new FoundgineNonRetryableOperationException("Operation has no workflow identity.");
            return c.idempotencyKey().substring(0, i);
        }

        protected static Map<?, ?> payload(FoundgineOperationContext c) {
            try {
                if (FoundgineJson.parse(c.payloadJson()) instanceof Map<?, ?> m) return m;
            } catch (RuntimeException ex) {
                // fall through
            }
            throw new FoundgineNonRetryableOperationException("Payload is not valid JSON.");
        }

        protected static String str(Map<?, ?> p, String name) { return p.get(name) instanceof String s ? s : null; }

        protected static List<String> strings(Map<?, ?> p, String name) {
            var set = new LinkedHashSet<String>();
            if (!p.containsKey(name)) return new ArrayList<>(set);
            if (!(p.get(name) instanceof List<?> list))
                throw new FoundgineNonRetryableOperationException("'" + name + "' must be an array of strings.");
            for (var e : list) {
                if (e instanceof String s && !s.isEmpty()) set.add(s);
                else throw new FoundgineNonRetryableOperationException("'" + name + "' must contain only non-empty strings.");
            }
            return new ArrayList<>(set);
        }

        protected static String requireUsername(Map<?, ?> p) {
            var u = str(p, "username");
            if (u == null || !KeycloakAdminClient.isValidUsername(u))
                throw new FoundgineNonRetryableOperationException(
                        "'username' is required (letters, digits, '.', '_', '@', '+', '-'; max 128).");
            return u;
        }

        protected static FoundgineOperationResult id(String key, String id) {
            var m = new LinkedHashMap<String, Object>();
            m.put(key, id);
            return new FoundgineOperationResult(AdminJson.write(m));
        }

        protected static boolean ownedBy(String tenantAttr, String provenance, String tenant, String workflow) {
            return tenant.equals(tenantAttr) && workflow.equals(provenance);
        }
    }

    /** Creates the top-level group for the workflow's tenant (named after the tenant id). Output: {@code {"groupId":"..."}}. */
    public static final class CreateTenantGroup extends KeycloakAdminOperation {
        public CreateTenantGroup(KeycloakAdminClient admin) { super("keycloak.create-tenant-group", admin); }

        @Override
        public FoundgineOperationResult execute(FoundgineOperationContext c, CancellationToken token) throws Exception {
            var tenant = tenant(c);
            var workflow = workflow(c);
            var existing = admin.findGroup(tenant);
            if (existing == null) {
                var id = admin.createGroup(tenant, tenant, workflow);
                if (id != null) return id("groupId", id);
                existing = admin.findGroup(tenant); // lost a creation race
            }
            if (existing != null && ownedBy(existing.tenantId(), existing.provisionedBy(), tenant, workflow))
                return id("groupId", existing.id()); // a retry of this very step
            throw new FoundgineNonRetryableOperationException(
                    "A Keycloak group for this tenant already exists and was not created by this process.");
        }
    }

    /** Compensation for {@link CreateTenantGroup}: removes the group only if this workflow created it. */
    public static final class DeleteTenantGroup extends KeycloakAdminOperation {
        public DeleteTenantGroup(KeycloakAdminClient admin) { super("keycloak.delete-tenant-group", admin); }

        @Override
        public FoundgineOperationResult execute(FoundgineOperationContext c, CancellationToken token) throws Exception {
            var tenant = tenant(c);
            var group = admin.findGroup(tenant);
            if (group != null && ownedBy(group.tenantId(), group.provisionedBy(), tenant, workflow(c))) admin.deleteGroup(group.id());
            return new FoundgineOperationResult();
        }
    }

    /**
     * Creates a user in the workflow's tenant. Payload: {@code username} (required), {@code email}, {@code firstName},
     * {@code lastName}, {@code roles} (subset of {@link FoundgineKeycloakOptions#adminAssignableRoles}),
     * {@code requiredActions} (subset of {@link FoundgineKeycloakOptions#adminAllowedRequiredActions}). Joins the tenant group
     * when one exists. Output: {@code {"userId":"..."}}.
     */
    public static final class CreateUser extends KeycloakAdminOperation {
        private final FoundgineKeycloakOptions options;

        public CreateUser(KeycloakAdminClient admin, FoundgineKeycloakOptions options) {
            super("keycloak.create-user", admin);
            this.options = options;
        }

        @Override
        public FoundgineOperationResult execute(FoundgineOperationContext c, CancellationToken token) throws Exception {
            var tenant = tenant(c);
            var workflow = workflow(c);
            var p = payload(c);

            var username = requireUsername(p);
            var email = str(p, "email");
            if (email != null && (email.length() > 254 || !email.contains("@")))
                throw new FoundgineNonRetryableOperationException("'email' is not valid.");

            var roles = strings(p, "roles");
            var actions = strings(p, "requiredActions");
            for (var r : roles)
                if (!options.adminAssignableRoles.contains(r))
                    throw new FoundgineNonRetryableOperationException("Role '" + r + "' is not assignable by onboarding.");
            for (var a : actions)
                if (!options.adminAllowedRequiredActions.contains(a))
                    throw new FoundgineNonRetryableOperationException("Required action '" + a + "' is not allowed.");

            var userId = ensureUser(new KeycloakNewUser(username, email, str(p, "firstName"), str(p, "lastName"), actions),
                    tenant, workflow);
            admin.assignRealmRoles(userId, roles);

            var group = admin.findGroup(tenant);
            if (group != null && tenant.equals(group.tenantId())) admin.addUserToGroup(userId, group.id());
            return id("userId", userId);
        }

        private String ensureUser(KeycloakNewUser user, String tenant, String workflow) throws Exception {
            // same message for "taken by someone else" however it was detected: it must not reveal other tenants' users
            var taken = new FoundgineNonRetryableOperationException("That username is not available.");
            var existing = admin.findUser(user.username());
            if (existing == null) {
                var id = admin.createUser(user, tenant, workflow);
                if (id != null) return id;
                existing = admin.findUser(user.username()); // lost a creation race
            }
            if (existing != null && ownedBy(existing.tenantId(), existing.provisionedBy(), tenant, workflow)) return existing.id();
            throw taken;
        }
    }

    /** Compensation for {@link CreateUser}: payload {@code username}; deletes only a user this workflow created in this tenant. */
    public static final class DeleteUser extends KeycloakAdminOperation {
        public DeleteUser(KeycloakAdminClient admin) { super("keycloak.delete-user", admin); }

        @Override
        public FoundgineOperationResult execute(FoundgineOperationContext c, CancellationToken token) throws Exception {
            var tenant = tenant(c);
            var user = admin.findUser(requireUsername(payload(c)));
            if (user != null && ownedBy(user.tenantId(), user.provisionedBy(), tenant, workflow(c))) admin.deleteUser(user.id());
            return new FoundgineOperationResult();
        }
    }

    /** Offboarding: payload {@code username}; disables any user of the workflow's tenant. Other tenants' users look like "not found". */
    public static final class DisableUser extends KeycloakAdminOperation {
        public DisableUser(KeycloakAdminClient admin) { super("keycloak.disable-user", admin); }

        @Override
        public FoundgineOperationResult execute(FoundgineOperationContext c, CancellationToken token) throws Exception {
            var tenant = tenant(c);
            var user = admin.findUser(requireUsername(payload(c)));
            if (user == null || !tenant.equals(user.tenantId()))
                throw new FoundgineNonRetryableOperationException("User not found in this tenant.");
            if (user.enabled()) admin.setUserEnabled(user.id(), false);
            return id("userId", user.id());
        }
    }
}
