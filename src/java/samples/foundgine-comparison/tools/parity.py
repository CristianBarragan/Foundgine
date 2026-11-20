#!/usr/bin/env python3
"""GraphQL parity/behaviour check.
  parity.py <foundgine-url> <baseline-url>   run identical queries on both apps and compare the data
  parity.py <baseline-url>                   run the same cases against the baseline alone: deny cases must be denied,
                                             allow cases must return data, and the row-level results are asserted exactly
"""
import json, subprocess, sys, urllib.request, urllib.error
urls = [u.rstrip("/") + "/graphql" for u in sys.argv[1:3]]
if not urls: sys.exit(__doc__)
def tok(*a): return subprocess.check_output([sys.executable, __file__.replace("parity.py", "mint_token.py"), *a], text=True).strip()
def post(url, q, t):
    req = urllib.request.Request(url, json.dumps({"query": q}).encode(), {"Content-Type": "application/json", **({"Authorization": "Bearer " + t} if t else {})})
    try:
        with urllib.request.urlopen(req) as r: return r.status, json.load(r)
    except urllib.error.HTTPError as e:
        try: return e.code, json.loads(e.read() or b"null")
        except Exception: return e.code, None
def norm(x):
    if isinstance(x, list): return sorted([norm(i) for i in x], key=lambda i: json.dumps(i, sort_keys=True))
    if isinstance(x, dict): return {k: norm(v) for k, v in sorted(x.items())}
    if isinstance(x, (int, float)) and not isinstance(x, bool): return round(float(x), 2)
    return x
def denied(code, body): return code in (401, 403) or ((body or {}).get("errors") is not None and not (body or {}).get("data"))
Q_ORDERS = "{ order { id status total customer { firstName } lines { quantity unitPrice product { sku name price } } } }"
cases = [
 ("manager: orders -> customer, lines -> product", tok("manager"), Q_ORDERS, True),
 ("manager: status filter + order by total", tok("manager"), '{ order(where:{status:{eq:"PENDING"}}, order:{total:DESC}) { id total } }', True),
 ("manager: cross-model filter (some line qty > 1)", tok("manager"), "{ order(where:{lines:{quantity:{max:{gt:1}}}}) { id } }", False),
 ("manager: customer -> orders -> lines -> product", tok("manager"), "{ customer { id lastName orders { id lines { product { name } } } } }", True),
 ("customer 1: orders (own only)", tok("customer", "1"), Q_ORDERS, True),
 ("customer 1: customer root (self only)", tok("customer", "1"), "{ customer { id email } }", True),
 ("customer 1: products", tok("customer", "1"), "{ product { id sku } }", True),
 ("customer 1: orderLine root denied", tok("customer", "1"), "{ orderLine { id } }", "deny"),
 ("no token", None, "{ product { id } }", "deny"),
 ("bad signature", tok("manager", "--bad-sig"), "{ product { id } }", "deny"),
 ("expired", tok("manager", "--expired"), "{ product { id } }", "deny"),
 ("missing tenant claim", tok("manager", "--no-tenant"), "{ product { id } }", "deny"),
]
# Cross-model filter dialects differ: Foundgine max(quantity) > 1 vs the baseline's some(quantity > 1).
alt = {"manager: cross-model filter (some line qty > 1)": "{ order(where:{lines:{some:{quantity:{gt:1}}}}) { id } }"}
# Exact expectations for the baseline-only mode (seed data in db/init.sql).
expect = {
 "customer 1: orders (own only)": lambda d: sorted(o["id"] for o in d["order"]) == [1, 2, 3],
 "customer 1: customer root (self only)": lambda d: [c["id"] for c in d["customer"]] == [1],
 "manager: status filter + order by total": lambda d: [o["id"] for o in d["order"]] == [4, 7, 1],
 "manager: cross-model filter (some line qty > 1)": lambda d: sorted(o["id"] for o in d["order"]) == [1, 2, 6, 8],
}
bad = 0
for name, t, q, mode in cases:
    bq = alt.get(name, q)
    if len(urls) == 2:
        fc, fb = post(urls[0], q, t); hc, hb = post(urls[1], bq, t)
        if mode == "deny": ok = denied(fc, fb) and denied(hc, hb)
        else: ok = fc == 200 and hc == 200 and norm((fb or {}).get("data")) == norm((hb or {}).get("data")) and (fb or {}).get("data") is not None
        detail = f"\n   foundgine={fc} {json.dumps(fb)[:300]}\n   baseline={hc} {json.dumps(hb)[:300]}"
    else:
        hc, hb = post(urls[0], bq, t)
        data = (hb or {}).get("data")
        if mode == "deny": ok = denied(hc, hb)
        else: ok = hc == 200 and data is not None and expect.get(name, lambda d: True)(data)
        detail = f"\n   baseline={hc} {json.dumps(hb)[:300]}"
    bad += not ok
    print(("PASS " if ok else "FAIL ") + name + ("" if ok else detail))
sys.exit(1 if bad else 0)
