#!/usr/bin/env python3
"""Same app, second protocol: calls Foundgine.Shop over MCP with the same JWTs and checks the same row-level rule.
usage: mcp_smoke.py <foundgine-url>   (start the app with Mcp__Enabled=true; run.sh does)"""
import json, subprocess, sys, urllib.request, urllib.error
URL = sys.argv[1].rstrip("/") + "/mcp"
def tok(*a): return subprocess.check_output([sys.executable, __file__.replace("mcp_smoke.py", "mint_token.py"), *a], text=True).strip()
INTENT = {"rootEntity": "Order", "selections": [{"field": "Id"}, {"field": "Total"}]}
def call(token):
    body = {"jsonrpc": "2.0", "id": 1, "method": "tools/call",
            "params": {"name": "foundgine_query_semantic", "arguments": {"intentJson": json.dumps(INTENT)}}}
    h = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream", **({"Authorization": "Bearer " + token} if token else {})}
    try:
        with urllib.request.urlopen(urllib.request.Request(URL, json.dumps(body).encode(), h)) as r: raw = r.read().decode(); code = r.status
    except urllib.error.HTTPError as e: return e.code, None
    data = [l[5:].strip() for l in raw.splitlines() if l.startswith("data:")] or [raw]   # the transport may frame replies as SSE
    return code, json.loads(data[0])
def rows(msg):
    text = "".join(c.get("text", "") for c in (msg.get("result", {}).get("content") or []))
    return text.count('"Id"') or text.count('"id"')   # loose on purpose: counts returned rows without assuming the result envelope
checks = []
mc, mm = call(tok("manager")); cc, cm = call(tok("customer", "1")); nc, _ = call(None)
checks.append(("manager reads all orders over MCP", mc == 200 and mm is not None and rows(mm) >= 8))
checks.append(("customer 1 sees only their own orders over MCP", cc == 200 and cm is not None and 0 < rows(cm) < (rows(mm) if mm else 0)))
checks.append(("no token -> 401", nc == 401))
for name, ok in checks: print(("PASS " if ok else "FAIL ") + name)
sys.exit(0 if all(ok for _, ok in checks) else 1)
