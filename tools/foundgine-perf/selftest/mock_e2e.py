"""Mock of Keycloak + CRM + ERP with async propagation delays. python mock_e2e.py PORT
Delays (s): sync 0.3, order 0.5, projection 0.5. Add DOWN file toggle: touch /tmp/mock_e2e_erp_down to 503 the ERP."""
import asyncio, json, os, re, sys, time, itertools
port = int(sys.argv[1]); ids = itertools.count(1)
S = {"owner": {}, "name": {}, "cust": {}, "quotes": {}, "orders": {}, "ship": {}, "inv": {}, "pay": {}}
now = time.monotonic
def respond(code, obj=None):
    b = json.dumps(obj).encode() if obj is not None else b""
    return code, b
TENANT = {"alice": "acme", "dave": "acme", "bob": "acme", "carol": "globex"}
def route(m, p, body, user):
    if p.startswith("/erp") and os.path.exists("/tmp/mock_e2e_erp_down"): return respond(503, {"e": "down"})
    if p.endswith("/openid-connect/token"):
        u = (body or {}).get("username", "alice") if isinstance(body, dict) else "alice"
        return respond(200, {"access_token": "tok-" + u, "expires_in": 300})
    if m == "GET" and p == "/crm/api/v1/customers": return respond(200, [{"id": k, "name": S["name"].get(k, "")} for k in list(S["cust"]) if S["owner"].get(k) == TENANT.get(user) or os.path.exists("/tmp/mock_e2e_leak")][:200])
    if (r := re.fullmatch(r"/crm/api/v1/customers/(\d+)(/360|/opportunities|/contacts)?", p)) and m == "GET":
        k = int(r[1])
        if k not in S["cust"]: return respond(404, {"e": "none"})
        if S["owner"].get(k) != TENANT.get(user) and not os.path.exists("/tmp/mock_e2e_leak"): return respond(404, {"e": "none"})
        return respond(200, {"id": k, "name": S["name"].get(k, "")})
    if m == "POST" and p == "/crm/api/v1/customers":
        i = next(ids); S["cust"][i] = now() + 0.3; S["owner"][i] = TENANT.get(user); S["name"][i] = (body or {}).get("name", ""); return respond(201, {"id": i})
    if r := re.fullmatch(r"/crm/api/v1/customers/(\d+)/opportunities", p):
        i = next(ids); S["quotes"][("o", i)] = int(r[1]); return respond(201, {"id": i})
    if r := re.fullmatch(r"/crm/api/v1/opportunities/(\d+)/quotes", p):
        i = next(ids); S["quotes"][i] = {"cust": S["quotes"][("o", int(r[1]))], "status": "Draft"}; return respond(201, {"quote": {"id": i}})
    if r := re.fullmatch(r"/crm/api/v1/quotes/(\d+)/submit", p):
        S["quotes"][int(r[1])]["status"] = "Submitted"; return respond(200, {"quote": {"status": "Submitted"}})
    if r := re.fullmatch(r"/crm/api/v1/quotes/(\d+)/accept", p):
        q = S["quotes"][int(r[1])]
        if now() < S["cust"][q["cust"]]: return respond(409, {"e": "customer not synced"})
        q.setdefault("order_at", now() + 0.5); return respond(200, {"quote": {"status": "Accepted"}})
    if r := re.fullmatch(r"/crm/api/v1/quotes/(\d+)", p):
        q = S["quotes"][int(r[1])]; done = "order_at" in q and now() >= q["order_at"]
        return respond(200, {"quote": {"id": int(r[1]), "status": "Ordered" if done else "Accepted", **({"erpOrderId": 1000 + int(r[1])} if done else {})}})
    if r := re.fullmatch(r"/erp/api/v1/orders/(\d+)/shipments", p):
        S["ship"][int(r[1])] = 1; return respond(201, {"erpShipmentId": int(r[1])})
    if re.fullmatch(r"/erp/api/v1/shipments/\d+/transitions", p): return respond(200, {})
    if r := re.fullmatch(r"/erp/api/v1/orders/(\d+)/invoices", p): return respond(201, {"erpInvoiceId": int(r[1]), "total": 100})
    if r := re.fullmatch(r"/erp/api/v1/invoices/(\d+)/payments", p): return respond(201, {"erpPaymentId": int(r[1])})
    if r := re.fullmatch(r"/erp/api/v1/payments/(\d+)/reconcile", p):
        S["pay"][int(r[1]) - 1000] = now() + 0.5; return respond(200, {})
    if r := re.fullmatch(r"/crm/api/v1/quotes/(\d+)/fulfilment", p):
        ok = S["pay"].get(int(r[1]), 1e18) <= now()
        row = lambda s: [{"status": s}] if ok else []
        return respond(200, {"shipments": row("Delivered"), "invoices": row("Paid"), "payments": row("Reconciled")})
    return respond(404, {"e": p})
async def handle(r, w):
    try:
        while True:
            line = await r.readline()
            if not line: break
            m, p, _ = line.decode().split(" ", 2); clen = 0; user = None; ctype = ""
            while (h := await r.readline()) not in (b"\r\n", b""):
                if h.lower().startswith(b"content-length"): clen = int(h.split(b":")[1])
                if h.lower().startswith(b"authorization"): user = h.decode().strip().split("tok-")[-1] if "tok-" in h.decode() else None
                if h.lower().startswith(b"content-type"): ctype = h.decode().lower()
            raw = await r.readexactly(clen) if clen else b""
            try: body = json.loads(raw) if raw and raw[:1] in b"{[" else (dict(x.split("=", 1) for x in raw.decode().split("&")) if raw and "urlencoded" in ctype else None)
            except ValueError: body = None
            try: code, b = route(m, p.split("?")[0], body, user)
            except KeyError: code, b = respond(404, {"e": "unknown"})
            w.write(b"HTTP/1.1 %d X\r\nContent-Type: application/json\r\nContent-Length: %d\r\n\r\n" % (code, len(b)) + b); await w.drain()
    except ConnectionError: pass
    finally: w.close()
async def main():
    s = await asyncio.start_server(handle, "127.0.0.1", port)
    async with s: await s.serve_forever()
asyncio.run(main())
