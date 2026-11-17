"""Arm authorization parity: before any performance number counts, every arm of a group must make the SAME authentication and
authorization decisions, and return the SAME rows, for the same Keycloak identities. A fast arm that skips a rule is not faster,
it is wrong. (Same idea as the CRM/ERP: the token is validated with issuer+audience+signature+lifetime, roles come from
realm_access.roles, tenant from tenant_id.)

Decisions:
  unauthenticated  HTTP 401                                         (no / garbage / forged / alg=none / edited token)
  deny             HTTP 403, a GraphQL error, null data, or every root field null (a write that matched nothing)
  allow            2xx without errors; optional `rows` rule: every row's field must be in an allowed set (row-level security)
A 5xx or timeout is `error`: it matches nothing, so a crashing arm fails. Arms of one `group` must also return identical canonical
rows for every `allow` vector. Vectors file names its `group`; only targets with that group (or no group) are exercised.
Stdlib only."""
import json, os, urllib.error, urllib.request
from .auth import TokenCache
from .authattack import variants
from .io import _wj, _wt, _rj
from .loadgen import canonical

def decide(status, payload):
    if status == 401: return "unauthenticated"
    if status == 403: return "deny"
    if status >= 500 or status == 0: return "error"
    try: doc = json.loads(payload)
    except ValueError: return "error"
    if not isinstance(doc, dict): return "error"
    if doc.get("errors") or doc.get("data") is None: return "deny"
    data = doc["data"]
    if isinstance(data, dict) and data and all(v is None for v in data.values()): return "deny"
    return "allow" if 200 <= status < 300 else "deny"

def _call(url, body, tok, timeout=30):
    hdrs = {"Content-Type": "application/json"}
    if tok: hdrs["Authorization"] = f"Bearer {tok}"
    req = urllib.request.Request(url, json.dumps(body).encode(), hdrs, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r: return r.status, r.read()
    except urllib.error.HTTPError as e: return e.code, e.read()
    except Exception: return 0, b""

def rows_ok(payload, rule):
    """rule: {"path": "salesOrder", "field": "customerId", "in": [1], "min_rows": 1} -> (ok, detail)"""
    try: rows = json.loads(payload)["data"][rule["path"]]
    except (ValueError, KeyError, TypeError): return False, "response has no data." + rule["path"]
    if not isinstance(rows, list): return False, f"data.{rule['path']} is not a list"
    if len(rows) < rule.get("min_rows", 0): return False, f"{len(rows)} rows, need >= {rule.get('min_rows', 0)}"
    bad = sorted({str(r.get(rule["field"])) for r in rows if r.get(rule["field"]) not in rule["in"]})
    return (not bad), (f"rows with {rule['field']} outside {rule['in']}: {bad[:5]}" if bad else "")

def run(cfg_path, vectors_path, outdir):
    cfg, vec = _rj(cfg_path), _rj(vectors_path)
    tokens = TokenCache.from_cfg(cfg.get("auth"))
    if tokens is None: raise SystemExit("armauthz needs an `auth` block in the config")
    group = vec.get("group")
    arms = {n: t for n, t in cfg["targets"].items() if t.get("authz", True) and (group is None or t.get("group", group) == group)}
    if len(arms) < 2: raise SystemExit(f"group {group!r}: need at least 2 arms, found {sorted(arms)}")
    tokens.prefetch({v["as"] for v in vec["vectors"] if v.get("as")})
    results, failures = [], []
    for v in vec["vectors"]:
        mode = v.get("token_mode", "valid")
        tok = None if mode == "none" else tokens.get(v["as"])
        if mode not in ("valid", "none"): tok = variants(tok)[mode]
        per = {}
        for name, t in arms.items():
            st, payload = _call(t["url"], v["body"], tok)
            d = decide(st, payload)
            e = {"status": st, "decision": d, "ok": d == v["expect"]["decision"], "detail": ""}
            if e["ok"] and d == "allow":
                if v["expect"].get("rows"): e["ok"], e["detail"] = rows_ok(payload, v["expect"]["rows"])
                e["hash"] = canonical(payload, v.get("ignore_keys", []), v.get("sort_lists_by", "id"))[0]
            per[name] = e
        hashes = {e["hash"] for e in per.values() if "hash" in e}
        same = len(hashes) <= 1
        ok = all(e["ok"] for e in per.values()) and same
        results.append({"id": v["id"], "expect": v["expect"]["decision"], "arms": per, "identical_rows": same, "ok": ok})
        if not ok:
            bad = [f"{n}={e['decision']}({e['status']}) {e['detail']}".strip() for n, e in per.items() if not e["ok"]]
            failures.append(f"{v['id']}: expected {v['expect']['decision']}; " + ("; ".join(bad) if bad else "arms returned different rows"))
    need = vec.get("min_vectors", 12)
    if len(vec["vectors"]) < need: failures.append(f"only {len(vec['vectors'])} vectors, need >= {need}")
    rep = {"pass": not failures, "group": group, "arms": sorted(arms), "vectors": len(vec["vectors"]), "failures": failures, "results": results}
    os.makedirs(outdir, exist_ok=True)
    tag = f"armauthz-{group}" if group else "armauthz"
    _wj(os.path.join(outdir, tag + ".json"), rep)
    _wt(os.path.join(outdir, tag + ".md"), f"# Arm authorization parity ({group})\n\n" + ("**PASS**" if rep["pass"] else "**FAIL**") +
        f" ({len(arms)} arms, {len(vec['vectors'])} vectors)\n\n" + "".join(f"- {f}\n" for f in failures))
    return rep
