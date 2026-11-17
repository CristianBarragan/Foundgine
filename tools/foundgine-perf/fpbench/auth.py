"""Keycloak bearer tokens for load runs.

Every request in an auth-enabled scenario names an identity (`"as": "alice"`). The harness fetches one access token per
identity from Keycloak (password grant), caches it client-side and refreshes it in the background before it expires, so token
issuance is NOT inside the measured request path. What IS measured, equally for every arm, is each arm's own per-request work:
JWT signature/expiry/issuer/audience validation plus its authorization rules (the CRM/ERP model: realm_access.roles + tenant_id).

Stdlib only. Blocking HTTP runs in an executor so it never stalls the load loop."""
import asyncio, json, time, urllib.parse, urllib.request


class TokenCache:
    def __init__(self, keycloak_url, realm, client_id, password, client_secret=None, margin_s=20.0, timeout_s=15.0):
        self.url = f"{keycloak_url.rstrip('/')}/realms/{realm}/protocol/openid-connect/token"
        self.client_id, self.password, self.client_secret = client_id, password, client_secret
        self.margin_s, self.timeout_s = margin_s, timeout_s
        self._tok = {}            # user -> (token, monotonic expiry)
        self.fetches = 0
        self.failures = 0

    @classmethod
    def from_cfg(cls, cfg):
        """cfg = the `auth` block of a run config; None/absent means auth is off."""
        if not cfg:
            return None
        return cls(cfg["keycloak_url"], cfg.get("realm", "supplychain"), cfg.get("client_id", "supplychain-bench"),
                   cfg.get("password", "dev-password"), cfg.get("client_secret"), cfg.get("refresh_margin_s", 20.0))

    def _fetch(self, user):
        form = {"grant_type": "password", "client_id": self.client_id, "username": user, "password": self.password}
        if self.client_secret:
            form["client_secret"] = self.client_secret
        req = urllib.request.Request(self.url, urllib.parse.urlencode(form).encode())
        with urllib.request.urlopen(req, timeout=self.timeout_s) as r:
            doc = json.load(r)
        self.fetches += 1
        return doc["access_token"], time.monotonic() + float(doc.get("expires_in", 60))

    def prefetch(self, users):
        for u in sorted(set(users)):
            self._tok[u] = self._fetch(u)

    def get(self, user):
        try:
            return self._tok[user][0]
        except KeyError:
            raise KeyError(f"no token for identity {user!r}; was it prefetched?") from None

    def remaining(self, user):
        return self._tok[user][1] - time.monotonic()

    async def keep_fresh(self, stop, interval=0.5):
        """Refresh every cached token with < margin_s left, until `stop` (asyncio.Event) is set."""
        loop = asyncio.get_running_loop()
        while not stop.is_set():
            for user in list(self._tok):
                if self.remaining(user) < self.margin_s:
                    try:
                        self._tok[user] = await loop.run_in_executor(None, self._fetch, user)
                    except Exception:
                        self.failures += 1   # keep the old token; an expired one shows up as 401s and invalidates the cell
            try:
                await asyncio.wait_for(stop.wait(), interval)
            except asyncio.TimeoutError:
                pass


def identities(scenario):
    """All identities a scenario's requests use."""
    return {r["as"] for r in scenario.get("requests", []) if r.get("as")}
