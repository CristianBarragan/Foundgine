# API layer

The semantic showcase entry point lives in the API layer, matching the physical layering used by `Foundgine.SupplyChain`.

# SupplyChain MCP authorization lab

This API is intentionally small and stateless. It is not a production authentication implementation; its fixed demo identities exist so the semantic authorization behavior can be exercised deterministically.

## Demo identities

| Actor | Tenant | Role |
|---|---|---|
| `alice` | `tenant-a` | Customer |
| `analyst-a` | `tenant-a` | Analyst |
| `operator-a` | `tenant-a` | WarehouseOperator |
| `manager-a` | `tenant-a` | SupplyChainManager |
| `analyst-b` | `tenant-b` | Analyst |

The server derives tenant and role from the actor/token pair. The MCP caller cannot simply send `role=SupplyChainManager` and have the server trust it. Wrong-token and unknown-actor probes therefore exercise the authentication boundary separately from semantic authorization.

`read_entity`, `write_entity`, and `policy_probe` additionally accept an optional `claims`
dictionary of caller-asserted context. Claims are validated by `ClientClaimsValidator` before
they ever reach the authorization policy: identity-shaped claims (`role`, `tenant`, `actor`, ...)
are rejected outright and fail the whole call, while recognized narrowing/evidence claims
(`scope`, `warehouse`, `reason`, `change_ticket`, ...) can only restrict what the authenticated
role already allows. See [`../GUIDE.md`](../GUIDE.md#claims-validation) for the full rules.

The endpoint is `/mcp` and the health endpoint is `/health`.


## Semantic contract used by the API

The API registers the composed `SupplyChainSemanticModel`. That model discovers the full Supply Chain structure from generated metadata, applies the two-entity `ManualSupplyChainSemanticModel` overlay, and then exposes the resulting contract to the semantic/MCP flow.
