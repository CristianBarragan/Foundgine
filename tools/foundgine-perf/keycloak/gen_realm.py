#!/usr/bin/env python3
"""Generate keycloak/supplychain-realm.json: the ONE identity provider used by all four supply-chain arms.

Mirrors the e2e CRM/ERP realm (infrastructure/keycloak/enterprise-realm.json):
  * realm roles in the `realm_access.roles` claim (the claim both the CRM and ERP read),
  * `tenant_id` from a user attribute, `customer_id` for customer-bound users,
  * one audience per API (`crm-api`, `erp-api` there; `supplychain-api` here) that every arm must validate,
  * the built-in `basic` (sub) and `profile` scopes are defined explicitly: Keycloak 26 does not create them when a realm import
    defines its own clientScopes (readiness/prepare-e2e.sh fix 10).
Dev only: HTTP, known passwords. Run: python3 keycloak/gen_realm.py"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
REALM, API_AUD, CLIENT = "supplychain", "supplychain-api", "supplychain-bench"

# Roles map onto the Java sample's Authorization.Role (CUSTOMER, ANALYST, WAREHOUSE_OPERATOR, SUPPLY_CHAIN_MANAGER)
# and the C# sample's capability groups (customer / operator / analyst / admin). See docs/SUPPLYCHAIN-ARMS.md.
ROLES = ["sc-customer", "sc-analyst", "sc-operator", "sc-manager"]
USERS = [  # username, realm roles, tenant_id (None = token has no tenant), customer_id (None = no customer binding)
    ("alice",   ["sc-customer"], "tenant-a", 1),
    ("bob",     ["sc-customer"], "tenant-a", 2),
    ("carol",   ["sc-operator"], "tenant-a", None),
    ("dave",    ["sc-analyst"],  "tenant-a", None),
    ("erin",    ["sc-analyst"],  "tenant-b", None),    # other tenant: must never see tenant-a rows
    ("admin",   ["sc-manager"],  "tenant-a", None),
    ("mallory", [],              "tenant-a", None),    # authenticated, tenant ok, no role: denied everything
    ("notenant", ["sc-manager"], None,       None),    # role but no tenant_id: denied (ERP ErpCaller.requireTenant)
]
TOKEN = {"id.token.claim": "false", "access.token.claim": "true", "introspection.token.claim": "true"}

def mapper(name, kind, **cfg):
    return {"name": name, "protocol": "openid-connect", "protocolMapper": kind, "consentRequired": False, "config": cfg}

def scope(name, mappers, in_token_scope="false"):
    return {"name": name, "protocol": "openid-connect", "protocolMappers": mappers,
            "attributes": {"include.in.token.scope": in_token_scope, "display.on.consent.screen": "false"}}

PROFILE = {"attributes": [
    {"name": "username", "validations": {"length": {"min": 3, "max": 255}}, "permissions": {"view": ["admin", "user"], "edit": ["admin"]}},
    {"name": "email", "validations": {"email": {}}, "permissions": {"view": ["admin", "user"], "edit": ["admin"]}},
    {"name": "firstName", "permissions": {"view": ["admin", "user"], "edit": ["admin"]}},
    {"name": "lastName", "permissions": {"view": ["admin", "user"], "edit": ["admin"]}},
    {"name": "tenant_id", "displayName": "Foundgine tenant",
     "validations": {"pattern": {"pattern": "^[A-Za-z0-9._-]{1,64}$", "error-message": "letters, digits, . _ -"}},
     "permissions": {"view": ["admin"], "edit": ["admin"]}, "multivalued": False},
    {"name": "customer_id", "displayName": "Supply-chain customer id",
     "validations": {"pattern": {"pattern": "^[0-9]{1,9}$", "error-message": "digits only"}},
     "permissions": {"view": ["admin"], "edit": ["admin"]}, "multivalued": False}]}

def build():
    scopes = [
        scope("basic", [mapper("sub", "oidc-sub-mapper", **{"access.token.claim": "true", "introspection.token.claim": "true"})]),
        scope("profile", [mapper("username", "oidc-usermodel-attribute-mapper",
                                 **{"user.attribute": "username", "claim.name": "preferred_username", "jsonType.label": "String", **TOKEN})], "true"),
        scope("supplychain-claims", [
            mapper("roles", "oidc-usermodel-realm-role-mapper", **{**TOKEN, "claim.name": "roles", "multivalued": "true", "jsonType.label": "String"}),
            mapper("realm-access-roles", "oidc-usermodel-realm-role-mapper",
                   **{**TOKEN, "claim.name": "realm_access.roles", "multivalued": "true", "jsonType.label": "String"}),
            mapper("tenant_id", "oidc-usermodel-attribute-mapper", **{**TOKEN, "user.attribute": "tenant_id", "claim.name": "tenant_id", "jsonType.label": "String"}),
            mapper("customer_id", "oidc-usermodel-attribute-mapper", **{**TOKEN, "user.attribute": "customer_id", "claim.name": "customer_id", "jsonType.label": "int"})]),
        scope("audience-supplychain-api", [mapper("audience-supplychain-api", "oidc-audience-mapper", **{**TOKEN, "included.client.audience": API_AUD})]),
    ]
    client = {"clientId": CLIENT, "enabled": True, "protocol": "openid-connect", "publicClient": True, "standardFlowEnabled": False,
              "directAccessGrantsEnabled": True, "serviceAccountsEnabled": False, "fullScopeAllowed": True,
              "defaultClientScopes": ["basic", "profile", "supplychain-claims", "audience-supplychain-api"]}
    api = {"clientId": API_AUD, "enabled": True, "protocol": "openid-connect", "publicClient": False, "bearerOnly": True}
    users = []
    for name, roles, tenant, cid in USERS:
        u = {"username": name, "enabled": True, "emailVerified": True, "email": f"{name}@example.test", "firstName": name.title(), "lastName": "Bench",
             "credentials": [{"type": "password", "value": "dev-password", "temporary": False}], "realmRoles": roles}
        attrs = {}
        if tenant: attrs["tenant_id"] = [tenant]
        if cid is not None: attrs["customer_id"] = [str(cid)]
        if attrs: u["attributes"] = attrs
        users.append(u)
    return {"realm": REALM, "enabled": True, "sslRequired": "none", "registrationAllowed": False, "accessTokenLifespan": 300,
            "clientScopes": scopes, "clients": [client, api], "roles": {"realm": [{"name": r} for r in ROLES]}, "users": users,
            "components": {"org.keycloak.userprofile.UserProfileProvider": [
                {"providerId": "declarative-user-profile", "subComponents": {}, "config": {"kc.user.profile.config": [json.dumps(PROFILE)]}}]}}

if __name__ == "__main__":
    out = os.path.join(HERE, "supplychain-realm.json")
    json.dump(build(), open(out, "w"), indent=2); print("wrote", out)
