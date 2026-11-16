"""Journeys for Foundgine-e2e-example (CRM .NET + ERP Java + Temporal). Mirrors e2e/run.sh.
cfg keys: crm_url, erp_url, sku_pool (list), qty, unit_price. Users: alice (crm-sales), dave (enterprise-admin)."""
import json, random

async def _quote(ctx, with_submit=True):
    c = ctx.cfg
    cust = await ctx.call("crm.create_customer", c["crm_url"], "POST", "/api/v1/customers", "alice",
        {"kind": "Business", "name": f"Perf Ltd {ctx.id}-{random.randrange(10**6)}",
         "email": f"perf-{ctx.id}-{random.randrange(10**9)}@example.test", "phone": "021 555 0100"})
    cid = cust["id"]
    opp = await ctx.call("crm.create_opportunity", c["crm_url"], "POST", f"/api/v1/customers/{cid}/opportunities", "alice",
                         {"name": "perf deal", "amount": 100})
    sku = c["sku_pool"][ctx.id % len(c["sku_pool"])]
    q = await ctx.call("crm.create_quote", c["crm_url"], "POST", f"/api/v1/opportunities/{opp['id']}/quotes", "alice",
        {"lines": [{"sku": sku, "description": "Widget", "quantity": c.get("qty", 1), "unitPrice": c.get("unit_price", 100)}]})
    qid = q["quote"]["id"]
    if with_submit:
        st = (await ctx.call("crm.submit_quote", c["crm_url"], "POST", f"/api/v1/quotes/{qid}/submit", "alice"))["quote"]["status"]
        if st == "PendingApproval":
            await ctx.call("crm.approve_quote", c["crm_url"], "POST", f"/api/v1/quotes/{qid}/approve", "dave")
    return cid, qid

async def customer_onboarding(ctx):
    """Time until a new customer is synchronised to the ERP (quote acceptance only succeeds once the mapping is Active)."""
    c = ctx.cfg
    _, qid = await _quote(ctx)
    await ctx.retry_until_2xx("wait.erp_customer_sync", c["crm_url"], "POST", f"/api/v1/quotes/{qid}/accept", "alice",
                              timeout=c.get("wait_timeout_s", 120))

async def customer_to_cash(ctx):
    """Quote -> order -> reservation -> shipment -> invoice -> payment -> reconcile -> CRM projection converged."""
    c = ctx.cfg
    _, qid = await _quote(ctx)
    await ctx.retry_until_2xx("wait.erp_customer_sync", c["crm_url"], "POST", f"/api/v1/quotes/{qid}/accept", "alice",
                              timeout=c.get("wait_timeout_s", 120))
    q = await ctx.wait_until("wait.quote_ordered", c["crm_url"], f"/api/v1/quotes/{qid}", "alice",
                             lambda j: j["quote"]["status"] == "Ordered", timeout=c.get("wait_timeout_s", 120))
    order = q["quote"]["erpOrderId"]
    ship = await ctx.call("erp.create_shipment", c["erp_url"], "POST", f"/api/v1/orders/{order}/shipments", "dave",
                          {"carrier": "NZ Post", "trackingNumber": f"TRK-{qid}", "idempotencyKey": f"perf-ship-{qid}"})
    for nxt in ("Shipped", "InTransit", "Delivered"):
        await ctx.call("erp.ship_transition", c["erp_url"], "POST", f"/api/v1/shipments/{ship['erpShipmentId']}/transitions", "dave", {"status": nxt})
    inv = await ctx.call("erp.create_invoice", c["erp_url"], "POST", f"/api/v1/orders/{order}/invoices", "dave", {})
    pay = await ctx.call("erp.create_payment", c["erp_url"], "POST", f"/api/v1/invoices/{inv['erpInvoiceId']}/payments", "dave",
                         {"amount": inv["total"], "idempotencyKey": f"perf-pay-{qid}"})
    await ctx.call("erp.reconcile", c["erp_url"], "POST", f"/api/v1/payments/{pay['erpPaymentId']}/reconcile", "dave")
    await ctx.wait_until("wait.crm_projection", c["crm_url"], f"/api/v1/quotes/{qid}/fulfilment", "alice",
        lambda f: len(f["shipments"]) == 1 and f["shipments"][0]["status"] == "Delivered"
                  and len(f["invoices"]) == 1 and f["invoices"][0]["status"] == "Paid"
                  and len(f["payments"]) == 1 and f["payments"][0]["status"] == "Reconciled",
        timeout=c.get("wait_timeout_s", 120))

async def read_paths(ctx):
    """Read-only journey: list + detail (closed loop recommended: --closed 1,8,32)."""
    c = ctx.cfg
    await ctx.call("crm.list_customers", c["crm_url"], "GET", "/api/v1/customers", "alice")
    if "quote_id" in c:
        await ctx.call("crm.quote_detail", c["crm_url"], "GET", f"/api/v1/quotes/{c['quote_id']}", "alice")

class Leak(Exception):
    """Raised (as a journey failure whose reason starts with LEAK) when tenant isolation is violated."""

def _denied(st):
    return not 200 <= st < 300

async def tenant_isolation(ctx):
    """Cross-tenant access attempts under load (readiness row R12). acme = alice, globex = carol.
    Positive controls first (owners CAN read their own data), so isolation cannot pass vacuously because everything 4xx'd.
    Any 2xx for the other tenant's data fails the journey with a reason starting 'LEAK'."""
    from ..journey import Fail
    c = ctx.cfg; crm = c["crm_url"]
    a = await ctx.call("crm.acme_create", crm, "POST", "/api/v1/customers", "alice",
        {"kind": "Business", "name": f"Perf Ltd ACME {ctx.id}-{random.randrange(10**6)}",
         "email": f"acme-{ctx.id}-{random.randrange(10**9)}@example.test", "phone": "021 555 0100"})
    g = await ctx.call("crm.globex_create", crm, "POST", "/api/v1/customers", "carol",
        {"kind": "Business", "name": f"Globex Ltd {ctx.id}-{random.randrange(10**6)}",
         "email": f"globex-{ctx.id}-{random.randrange(10**9)}@example.test", "phone": "021 555 0101"})
    await ctx.call("control.acme_reads_own", crm, "GET", f"/api/v1/customers/{a['id']}", "alice")
    await ctx.call("control.globex_reads_own", crm, "GET", f"/api/v1/customers/{g['id']}", "carol")
    probes = [("carol", f"/api/v1/customers/{a['id']}"), ("carol", f"/api/v1/customers/{a['id']}/360"),
              ("carol", f"/api/v1/customers/{a['id']}/opportunities"), ("carol", f"/api/v1/customers/{a['id']}/contacts"),
              ("alice", f"/api/v1/customers/{g['id']}"), ("alice", f"/api/v1/customers/{g['id']}/360")]
    for user, path in probes:
        st, js = await ctx.raw(crm, "GET", path, user)
        if not _denied(st): raise Fail(f"LEAK: {user} got HTTP {st} for {path}")
    # list endpoints must not contain the other tenant's rows
    for user, needle in (("carol", "Perf Ltd ACME"), ("alice", "Globex Ltd")):
        st, js = await ctx.raw(crm, "GET", f"/api/v1/customers?q={needle.split()[0]}&take=200", user)
        body = json.dumps(js) if js is not None else ""
        if 200 <= st < 300 and needle in body: raise Fail(f"LEAK: {user} list contains '{needle}'")
    # neighbouring ids (global sequence): any acme-named row visible to carol is a leak
    for d in (-2, -1, 1, 2):
        st, js = await ctx.raw(crm, "GET", f"/api/v1/customers/{a['id'] + d}", "carol")
        if 200 <= st < 300 and "Perf Ltd ACME" in json.dumps(js): raise Fail(f"LEAK: carol read acme row id {a['id'] + d}")

