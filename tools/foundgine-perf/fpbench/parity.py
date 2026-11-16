"""Cross-surface authorization parity (R13): the same actor asking for the same thing through REST, GraphQL and MCP-over-HTTP must get the
same decision. Vectors are data (readiness/parity-vectors.json); a vector with "todo": true is a placeholder and makes the check not_run.
decision: allow = 2xx with no GraphQL/JSON-RPC error; deny = 401/403/404 or an error payload. Anything else (5xx, timeout) is an ERROR, never a decision."""
import json, os, urllib.error, urllib.parse, urllib.request
from . import readiness
from .io import _rj

def token(kc, realm, client, user, pw):
    body = urllib.parse.urlencode({"grant_type": "password", "client_id": client, "username": user, "password": pw}).encode()
    with urllib.request.urlopen(urllib.request.Request(f"{kc.rstrip('/')}/realms/{realm}/protocol/openid-connect/token", body), timeout=15) as r:
        return json.load(r)["access_token"]

def decide(status, payload):
    if status in (401, 403, 404): return "deny"
    if status >= 500 or status == 0: return "error"
    if isinstance(payload, dict) and (payload.get("errors") or payload.get("error")): return "deny"
    return "allow" if 200 <= status < 300 else "deny"

def call(base, spec, tok):
    data = json.dumps(spec["body"]).encode() if spec.get("body") is not None else None
    req = urllib.request.Request(base + spec["path"], data, {"Authorization": f"Bearer {tok}", "Content-Type": "application/json"}, method=spec.get("method", "GET"))
    try:
        with urllib.request.urlopen(req, timeout=30) as r: st, raw = r.status, r.read()
    except urllib.error.HTTPError as e: st, raw = e.code, e.read()
    except Exception: return "error"
    try: pl = json.loads(raw) if raw else None
    except ValueError: pl = None
    return decide(st, pl)

def run(cfg_path, vectors_path, evdir, min_vectors=10, cid="R13"):
    cfg, vs = _rj(cfg_path), _rj(vectors_path)["vectors"]
    if not vs or any(v.get("todo") for v in vs): return readiness.record(evdir, cid, "not_run", detail="parity vectors missing or still contain placeholders (todo)")
    if len(vs) < min_vectors: return readiness.record(evdir, cid, "not_run", {"vectors": len(vs)}, f">= {min_vectors} vectors", "too few vectors to claim parity")
    a = cfg["auth"]; bases = {"rest": cfg["crm_url"], "erp": cfg["erp_url"], **cfg.get("surfaces", {})}
    toks, bad, surfaces = {}, [], set()
    for v in vs:
        if v["user"] not in toks: toks[v["user"]] = token(a["keycloak_url"], a.get("realm", "enterprise"), a.get("client_id", "enterprise-web"), v["user"], a.get("password", "dev-password"))
        got = {s: call(bases[spec.get("base", s if s in bases else "rest")], spec, toks[v["user"]]) for s, spec in v["surfaces"].items()}; surfaces |= set(got)
        if len(set(got.values())) != 1 or "error" in got.values() or (v.get("expect") and got and set(got.values()) != {v["expect"]}):
            bad.append(f"{v['name']}: {got} (expected {v.get('expect', 'agreement')})")
    if len(surfaces) < 3: return readiness.record(evdir, cid, "not_run", {"surfaces": sorted(surfaces)}, "REST, GraphQL and MCP all covered", "fewer than three surfaces in the vectors")
    return readiness.record(evdir, cid, "fail" if bad else "pass", {"vectors": len(vs), "surfaces": sorted(surfaces), "disagreements": len(bad)},
                            "identical decisions on every vector, matching the expected outcome", "; ".join(bad[:5]), artifacts=[vectors_path])
