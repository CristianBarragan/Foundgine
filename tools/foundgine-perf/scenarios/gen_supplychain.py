#!/usr/bin/env python3
"""Emit the supply-chain perf scenarios and authorization vectors. Two runtimes, two groups, never mixed:
   csharp: Hot Chocolate + plain EF Core   vs  Foundgine GraphQL + Foundgine EF provider + Foundgine EF migrations
   java  : Spring GraphQL + plain Hibernate vs  Foundgine GraphQL + Foundgine JPA provider + foundgine-migrations
Each group uses its own sample's domain (src/csharp/samples/Foundgine.SupplyChain, src/java/samples/foundgine-supply-chain).
Every request names the Keycloak identity it runs as (fpbench/auth.py). GraphQL surface = what Foundgine's schema adapter exposes
(root field = camelCase entity name; args first/order/offset/after/where; create/update/delete/upsert<Entity>(input, where) with
`where` plain equality: {id: 1}); the baseline arms must expose the SAME surface (docs/SUPPLYCHAIN-ARMS.md).
Run: python3 scenarios/gen_supplychain.py"""
import json, os
HERE = os.path.dirname(os.path.abspath(__file__))

def gql(query, op, variables=None, as_=None):
    r = {"method": "POST", "path": "", "body": {"query": query, "operationName": op}}
    if variables is not None: r["body"]["variables"] = variables
    if as_: r["as"] = as_
    return r

def write(path, doc):
    json.dump(doc, open(os.path.join(HERE, path), "w"), indent=1)

# ------------------------------------------------------------------ C# domain
CS_ORDERS = """query Orders { salesOrder(first: 50) { id customerId status totalAmount
 lines { id productId quantity unitPrice } shipments { id trackingNumber status } } }"""
CS_PRODUCTS = "query Products { catalogProduct(first: 50) { id name sku unitPrice supplierId categoryId } }"
CS_SUPPLIERS = "query Suppliers { supplier(first: 50) { id name email } }"
CS_SHIPMENTS = "query Shipments { shipment(first: 50) { id orderId status } }"
CS_SHIP_UPDATE = """mutation SetShipmentStatus($input: ShipmentInput!, $where: ShipmentWhereInput!) {
 updateShipment(input: $input, where: $where) { id status } }"""
CS_ORDER_CREATE = """mutation PlaceOrder($input: SalesOrderInput!) {
 createSalesOrder(input: $input) { id customerId status totalAmount } }"""
CS_ORDER_TWICE = """mutation PlaceTwo($input: SalesOrderInput!) {
 first: createSalesOrder(input: $input) { id } second: createSalesOrder(input: $input) { id } }"""
CS_ORDERS_AND_SHIPMENTS = "query Both { salesOrder(first: 5) { id } shipment(first: 5) { id orderId status } }"
def cs_order(customer): return {"customerId": customer, "status": "Pending", "totalAmount": 70.0,
    "lines": [{"productId": 1, "quantity": 2, "unitPrice": 10.0}, {"productId": 2, "quantity": 1, "unitPrice": 50.0}]}
SHIP_VARS = {"input": {"status": "In Transit"}, "where": {"id": 1}}

for name, reqs, extra in [
    ("sc-cs-orders-customer", [gql(CS_ORDERS, "Orders", as_="alice")], {}),
    ("sc-cs-orders-operator", [gql(CS_ORDERS, "Orders", as_="carol")], {}),
    ("sc-cs-products", [gql(CS_PRODUCTS, "Products", as_="bob")], {}),
    # one cell, four identities: the arm must apply a DIFFERENT row filter to consecutive requests (catches plan-cache leaks across users)
    ("sc-cs-orders-mixed", [gql(CS_ORDERS, "Orders", as_=u) for u in ("alice", "bob", "carol", "admin")], {}),
    ("sc-cs-shipment-update", [gql(CS_SHIP_UPDATE, "SetShipmentStatus", SHIP_VARS, as_="carol")], {"ignore_keys": ["id"]}),
    ("sc-cs-order-create", [gql(CS_ORDER_CREATE, "PlaceOrder", {"input": cs_order(1)}, as_="alice")], {"ignore_keys": ["id"]}),
]:
    write(name + ".json", {"name": name, "requests": reqs, "sort_lists_by": "id", **extra})

def vec(id_, as_, body, decision, rows=None, mode="valid", **kw):
    v = {"id": id_, "token_mode": mode, "body": {"query": body[0], "operationName": body[1], **({"variables": body[2]} if len(body) > 2 else {})},
         "expect": {"decision": decision, **({"rows": rows} if rows else {})}, **kw}
    if as_: v["as"] = as_
    return v

O, P, S, SH = (CS_ORDERS, "Orders"), (CS_PRODUCTS, "Products"), (CS_SUPPLIERS, "Suppliers"), (CS_SHIPMENTS, "Shipments")
U, C = (CS_SHIP_UPDATE, "SetShipmentStatus", SHIP_VARS), None
cs = [
    vec("cs-alice-own-orders", "alice", O, "allow", {"path": "salesOrder", "field": "customerId", "in": [1], "min_rows": 1}),
    vec("cs-bob-own-orders", "bob", O, "allow", {"path": "salesOrder", "field": "customerId", "in": [2], "min_rows": 1}),
    vec("cs-operator-all-orders", "carol", O, "allow", {"path": "salesOrder", "field": "customerId", "in": [1, 2, 3, 4, 5, 6, 7, 8, 9], "min_rows": 2}),
    vec("cs-manager-all-orders", "admin", O, "allow", {"path": "salesOrder", "field": "customerId", "in": [1, 2, 3, 4, 5, 6, 7, 8, 9], "min_rows": 2}),
    vec("cs-analyst-no-orders", "dave", O, "deny"),
    vec("cs-customer-products", "alice", P, "allow", {"path": "catalogProduct", "field": "id", "in": list(range(1, 100)), "min_rows": 1}),
    vec("cs-customer-no-suppliers", "alice", S, "deny"),
    vec("cs-analyst-suppliers", "dave", S, "allow", {"path": "supplier", "field": "id", "in": list(range(1, 100)), "min_rows": 1}),
    vec("cs-customer-no-root-shipments", "alice", SH, "deny"),
    vec("cs-norole-denied", "mallory", P, "deny"),
    vec("cs-notenant-denied", "notenant", P, "deny"),          # role but no tenant_id: ERP ErpCaller.requireTenant / Foundgine bridge
    vec("cs-operator-updates-shipment", "carol", U, "allow", ignore_keys=["id"]),
    vec("cs-customer-cannot-update-shipment", "alice", U, "deny"),
    vec("cs-analyst-cannot-update-shipment", "dave", U, "deny"),
    vec("cs-customer-orders-for-self", "alice", (CS_ORDER_CREATE, "PlaceOrder", {"input": cs_order(1)}), "allow", ignore_keys=["id"]),
    vec("cs-customer-cannot-order-for-other", "alice", (CS_ORDER_CREATE, "PlaceOrder", {"input": cs_order(2)}), "deny"),
] + [
    # Failure and boundary cases. Every one must be refused by BOTH arms with a GraphQL error / null data (never a 5xx),
    # and the baseline's order create is a single EF Core transaction, so a bad line cannot leave an order behind.
    vec("cs-manager-updates-shipment", "admin", U, "allow", ignore_keys=["id"]),
    vec("cs-operator-unknown-shipment", "carol", (CS_SHIP_UPDATE, "SetShipmentStatus", {"input": {"status": "In Transit"}, "where": {"id": 999999}}), "deny"),
    vec("cs-operator-invalid-shipment-status", "carol", (CS_SHIP_UPDATE, "SetShipmentStatus", {"input": {"status": "Teleported"}, "where": {"id": 1}}), "deny"),
    vec("cs-manager-cannot-create-order", "admin", (CS_ORDER_CREATE, "PlaceOrder", {"input": cs_order(1)}), "deny"),
    vec("cs-operator-cannot-create-order", "carol", (CS_ORDER_CREATE, "PlaceOrder", {"input": cs_order(1)}), "deny"),
    vec("cs-customer-order-unknown-product", "alice", (CS_ORDER_CREATE, "PlaceOrder", {"input": {**cs_order(1), "lines": [{"productId": 999999, "quantity": 1, "unitPrice": 10.0}]}}), "deny"),
    vec("cs-customer-two-orders-one-request", "alice", (CS_ORDER_TWICE, "PlaceTwo", {"input": cs_order(1)}), "deny"),
    vec("cs-customer-mixed-roots-with-shipments", "alice", (CS_ORDERS_AND_SHIPMENTS, "Both"), "deny"),
] + [vec(f"cs-token-{m}", "alice", O, "unauthenticated", mode=m) for m in ("none", "garbage", "bad_signature", "alg_none", "edited_payload")]
write("vectors-supplychain-csharp.json", {"group": "csharp", "min_vectors": 12, "vectors": cs})

# ------------------------------------------------------------------ Java domain (3 tenant-aware entities)
J_SUP = "query Suppliers { supplier(first: 50) { id name country riskScore tenantId } }"
J_SUP_SAFE = "query SuppliersNoRisk { supplier(first: 50) { id name country tenantId } }"
J_WH = "query Warehouses { warehouse(first: 50) { id businessUnitId name tenantId } }"
J_PROD = "query Products { product(first: 50) { id sku name category safetyStock } }"
J_WH_UPDATE = """mutation RenameWarehouse($input: WarehouseInput!, $where: WarehouseWhereInput!) {
 updateWarehouse(input: $input, where: $where) { id name } }"""
WH1 = {"input": {"name": "Austin DC"}, "where": {"id": 1}}       # idempotent: same name every time
WH3 = {"input": {"name": "Hidden DC"}, "where": {"id": 3}}       # tenant-b row
for name, reqs in [
    ("sc-java-suppliers-analyst", [gql(J_SUP, "Suppliers", as_="dave")]),
    ("sc-java-products", [gql(J_PROD, "Products", as_="alice")]),
    ("sc-java-warehouses-operator", [gql(J_WH, "Warehouses", as_="carol")]),
    ("sc-java-suppliers-mixed", [gql(J_SUP_SAFE, "SuppliersNoRisk", as_=u) for u in ("dave", "erin", "carol", "admin")]),
    ("sc-java-warehouse-update", [gql(J_WH_UPDATE, "RenameWarehouse", WH1, as_="carol")]),
]:
    write(name + ".json", {"name": name, "requests": reqs, "sort_lists_by": "id", **({"ignore_keys": ["id"]} if "update" in name else {})})

JS, JSS, JW, JP, JU = (J_SUP, "Suppliers"), (J_SUP_SAFE, "SuppliersNoRisk"), (J_WH, "Warehouses"), (J_PROD, "Products"), (J_WH_UPDATE, "RenameWarehouse", WH1)
java = [
    vec("j-analyst-tenant-a-suppliers", "dave", JS, "allow", {"path": "supplier", "field": "tenantId", "in": ["tenant-a"], "min_rows": 2}),
    vec("j-other-tenant-suppliers", "erin", JS, "allow", {"path": "supplier", "field": "tenantId", "in": ["tenant-b"], "min_rows": 1}),
    vec("j-manager-suppliers", "admin", JS, "allow", {"path": "supplier", "field": "tenantId", "in": ["tenant-a"], "min_rows": 2}),
    vec("j-operator-supplier-risk-denied", "carol", JS, "deny"),                       # field rule: riskScore is analyst/manager only
    vec("j-operator-suppliers-without-risk", "carol", JSS, "allow", {"path": "supplier", "field": "tenantId", "in": ["tenant-a"], "min_rows": 2}),
    vec("j-customer-no-suppliers", "alice", JSS, "deny"),
    vec("j-operator-warehouses", "carol", JW, "allow", {"path": "warehouse", "field": "tenantId", "in": ["tenant-a"], "min_rows": 2}),
    vec("j-analyst-no-warehouses", "dave", JW, "deny"),
    vec("j-customer-products", "alice", JP, "allow", {"path": "product", "field": "id", "in": [1, 2], "min_rows": 2}),
    vec("j-norole-denied", "mallory", JP, "deny"),
    vec("j-notenant-denied", "notenant", JP, "deny"),
    vec("j-operator-renames-warehouse", "carol", JU, "allow", ignore_keys=["id"]),
    vec("j-analyst-cannot-write", "dave", JU, "deny"),
    vec("j-operator-cannot-write-other-tenant", "carol", (J_WH_UPDATE, "RenameWarehouse", WH3), "deny"),
] + [vec(f"j-token-{m}", "dave", JS, "unauthenticated", mode=m) for m in ("none", "garbage", "bad_signature", "alg_none", "edited_payload")]
write("vectors-supplychain-java.json", {"group": "java", "min_vectors": 12, "vectors": java})
print("wrote sc-cs-*, sc-java-*, vectors-supplychain-*.json")
