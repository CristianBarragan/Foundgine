"""Bearer-token hardening (R22): every protected endpoint must reject a request with no token, a garbage token, a token whose
signature was altered, an `alg: none` token and a token whose payload was edited under the original signature.
Anything other than 401/403 is a failure; a 5xx or timeout is an error (inconclusive), never a pass. A valid token must still be accepted
(positive control), otherwise 'rejected everything' would pass vacuously."""
import base64, json, urllib.error, urllib.request
from . import readiness
from .io import _rj
from .parity import token

def _b64(b): return base64.urlsafe_b64encode(b).rstrip(b"=").decode()
def _unb64(s): return base64.urlsafe_b64decode(s + "=" * (-len(s) % 4))

def variants(tok):
    h, p, s = tok.split(".")
    pl = json.loads(_unb64(p)); pl["preferred_username"] = "mallory"; pl["sub"] = "00000000-0000-0000-0000-000000000000"
    return {"no_token": None, "garbage": "not-a-jwt", "bad_signature": f"{h}.{p}.{s[:-4]}{'AAAA' if s[-4:] != 'AAAA' else 'BBBB'}",
            "alg_none": f"{_b64(b'{\"alg\":\"none\",\"typ\":\"JWT\"}')}.{p}.", "edited_payload": f"{h}.{_b64(json.dumps(pl).encode())}.{s}"}

def status(url, tok):
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {tok}"} if tok else {})
    try:
        with urllib.request.urlopen(req, timeout=15) as r: return r.status
    except urllib.error.HTTPError as e: return e.code
    except Exception: return 0

def run(cfg_path, evdir, endpoints, user="alice", cid="R22"):
    cfg = _rj(cfg_path); a = cfg["auth"]
    if not endpoints: return readiness.record(evdir, cid, "not_run", detail="no endpoints given")
    tok = token(a["keycloak_url"], a.get("realm", "enterprise"), a.get("client_id", "enterprise-web"), user, a.get("password", "dev-password"))
    bad, errors, n = [], [], 0
    for url in endpoints:
        ok = status(url, tok)
        if not 200 <= ok < 300: errors.append(f"{url}: valid token got {ok} (positive control failed)"); continue
        for name, t in variants(tok).items():
            n += 1; st = status(url, t)
            if st in (401, 403): continue
            (errors if st >= 500 or st == 0 else bad).append(f"{url} [{name}]: HTTP {st}")
    m = {"endpoints": len(endpoints), "attempts": n, "accepted": len(bad), "inconclusive": len(errors)}
    if bad: return readiness.record(evdir, cid, "fail", m, "401/403 for every forged or missing token", "; ".join(bad[:5]))
    if errors: return readiness.record(evdir, cid, "not_run", m, "no 5xx/timeouts, positive control passes", "; ".join(errors[:5]))
    return readiness.record(evdir, cid, "pass", m, "401/403 for every forged or missing token, valid token accepted")
