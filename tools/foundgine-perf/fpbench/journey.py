"""Business-journey load runner (multi-step flows with waits for async propagation).

A journey is `async def journey(ctx)`. Journey latency is measured from the INTENDED arrival
time (open loop) so queueing and backlog under overload are counted. Steps are timed
individually, and waits for Temporal/outbox propagation are separate steps (`ctx.wait_*`),
so the report can show where end-to-end time goes.
"""
from .io import _wj, _wt, _rj
import asyncio, gzip, importlib, json, os, random, time
from urllib.parse import urlsplit, urlencode
from .loadgen import Conn
from .sampler import Sampler
from .stats import summarize_latencies

class Fail(Exception):
    pass

class Auth:
    """Keycloak password-grant tokens, fetched before the measured window and refreshed in background."""
    def __init__(self, keycloak_url, realm, client_id, users, password):
        self.kc, self.realm, self.client, self.users, self.pw = keycloak_url, realm, client_id, users, password
        self.tokens = {}
    async def _fetch(self, user):
        u = urlsplit(self.kc)
        c = Conn(u.hostname, u.port or 80)
        body = urlencode({"grant_type": "password", "client_id": self.client, "username": user, "password": self.pw})
        st, pl = await c.request("POST", f"{u.path.rstrip('/')}/realms/{self.realm}/protocol/openid-connect/token", body,
                                 {"Content-Type": "application/x-www-form-urlencoded"}, 15)
        c.close()
        if st != 200:
            raise Fail(f"token for {user}: HTTP {st}")
        d = json.loads(pl)
        self.tokens[user] = d["access_token"]
        return d.get("expires_in", 300)
    async def init(self):
        exp = [await self._fetch(u) for u in self.users]
        self._task = asyncio.create_task(self._refresh(min(exp) * 0.6))
    async def _refresh(self, every):
        while True:
            await asyncio.sleep(every)
            for u in self.users:
                try: await self._fetch(u)
                except Exception: pass
    def stop(self):
        self._task.cancel()

class Ctx:
    def __init__(self, cfg, auth, ident):
        self.cfg, self.auth, self.id = cfg, auth, ident
        self.steps, self.conns, self.vars = {}, {}, {}
    def _conn(self, base):
        u = urlsplit(base)
        k = (u.hostname, u.port or 80)
        if k not in self.conns:
            self.conns[k] = Conn(*k)
        return self.conns[k], u.path.rstrip("/")
    def _add(self, step, t0):
        self.steps[step] = self.steps.get(step, 0.0) + (time.perf_counter() - t0) * 1000
    async def raw(self, base, method, path, user, body=None, timeout=30):
        c, prefix = self._conn(base)
        data = json.dumps(body) if isinstance(body, (dict, list)) else body
        h = {"Authorization": f"Bearer {self.auth.tokens[user]}", "Content-Type": "application/json"}
        st, pl = await c.request(method, prefix + path, data, h, timeout)
        try: js = json.loads(pl) if pl else None
        except ValueError: js = None
        return st, js
    async def call(self, step, base, method, path, user, body=None):
        t0 = time.perf_counter()
        try:
            st, js = await self.raw(base, method, path, user, body)
        finally:
            self._add(step, t0)
        if not 200 <= st < 300:
            raise Fail(f"{step}: HTTP {st}")
        return js
    async def retry_until_2xx(self, step, base, method, path, user, body=None, timeout=180, every=0.1):
        """Waits for an async precondition (e.g. ERP customer sync) by retrying; all time is the `step`."""
        t0, end = time.perf_counter(), time.perf_counter() + timeout
        try:
            while True:
                st, js = await self.raw(base, method, path, user, body)
                if 200 <= st < 300: return js
                if st not in (404, 409, 422, 425, 503): raise Fail(f"{step}: HTTP {st}")
                if time.perf_counter() > end: raise Fail(f"{step}: timeout")
                await asyncio.sleep(every)
        finally:
            self._add(step, t0)
    async def wait_until(self, step, base, path, user, pred, timeout=180, every=0.1):
        t0, end = time.perf_counter(), time.perf_counter() + timeout
        try:
            while True:
                st, js = await self.raw(base, "GET", path, user)
                if 200 <= st < 300 and js is not None and pred(js): return js
                if time.perf_counter() > end: raise Fail(f"{step}: timeout")
                await asyncio.sleep(every)
        finally:
            self._add(step, t0)
    def close(self):
        for c in self.conns.values(): c.close()

def _resolve(spec):
    mod, fn = spec.split(":")
    return getattr(importlib.import_module(mod), fn)

async def _probe_loop(name, cmd, interval, series, errors):
    while True:
        try:
            p = await asyncio.create_subprocess_shell(cmd, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE)
            out, err = await asyncio.wait_for(p.communicate(), 10)
            series.append(float(out.decode().strip().split()[0]))
        except Exception as e:
            errors.append(f"{name}: {e!r}")
        await asyncio.sleep(interval)

async def _post_checks(jcfg):
    """Run `post_cmd` checks ([{name, cmd}]) after the load, retrying each until `post_timeout_s` (eventual consistency).
    rc 0 = invariant holds. Used for data-integrity checks after fault injection."""
    out, deadline_s = [], jcfg.get("post_timeout_s", 120)
    for chk in jcfg.get("post_cmd", []):
        end, rc, text = time.perf_counter() + deadline_s, 1, ""
        while True:
            p = await asyncio.create_subprocess_shell(chk["cmd"], stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT)
            o, _ = await p.communicate(); rc, text = p.returncode, o.decode(errors="replace")[-2000:]
            if rc == 0 or time.perf_counter() > end: break
            await asyncio.sleep(jcfg.get("post_retry_s", 5))
        out.append({"name": chk["name"], "rc": rc, "output": text})
    return out

async def run_cell(jcfg, cfg, auth, mode, level, outdir, duration, rng):
    fn = _resolve(jcfg["fn"])
    sched = None
    if mode == "staged":   # [{rate, s}, ...]: step the arrival rate (overload then recovery); intended times follow the schedule
        sched, t = [], 0.0
        for seg in jcfg["staged"]:
            sched += [t + k / seg["rate"] for k in range(int(seg["rate"] * seg["s"]))]; t += seg["s"]
        duration = t
    results, t_begin = [], time.perf_counter()
    stop_at = t_begin + duration
    samplers = {n: Sampler(s, 1.0) for n, s in cfg.get("resources", {}).items()}
    probes = {n: ([], []) for n in cfg.get("probes", {})}
    ptasks = [asyncio.create_task(_probe_loop(n, c, 1.0, *probes[n])) for n, c in cfg.get("probes", {}).items()]
    for s in samplers.values(): s.start()
    events_log, etasks = [], []
    async def fire_event(ev):
        await asyncio.sleep(ev["at_s"])
        events_log.append({"name": ev["name"], "t": time.perf_counter() - t_begin})
        if ev.get("cmd"):
            p = await asyncio.create_subprocess_shell(ev["cmd"]); await p.wait()
    etasks = [asyncio.create_task(fire_event(e)) for e in jcfg.get("events", [])]

    async def one(i, intended):
        ctx = Ctx(cfg, auth, i)
        start = time.perf_counter()
        ok, why = True, None
        try:
            await asyncio.wait_for(fn(ctx), jcfg.get("journey_timeout_s", 300))
        except asyncio.TimeoutError: ok, why = False, "journey timeout"
        except Fail as e: ok, why = False, str(e)
        except Exception as e: ok, why = False, f"{type(e).__name__}: {e}"
        end = time.perf_counter()
        ctx.close()
        results.append({"i": i, "intended": intended - t_begin, "start": start - t_begin, "end": end - t_begin,
                        "ok": ok, "why": why, "steps": ctx.steps})

    sem = asyncio.Semaphore(jcfg.get("max_inflight", 2000))
    tasks = []
    try:
        if mode == "open":
            n = int(level * duration)
            for i in range(n):
                intended = t_begin + i / level
                d = intended - time.perf_counter()
                if d > 0: await asyncio.sleep(d)
                async def go(i=i, intended=intended):
                    async with sem: await one(i, intended)
                tasks.append(asyncio.create_task(go()))
        elif mode == "staged":
            for i, off in enumerate(sched):
                intended = t_begin + off
                d = intended - time.perf_counter()
                if d > 0: await asyncio.sleep(d)
                async def go(i=i, intended=intended):
                    async with sem: await one(i, intended)
                tasks.append(asyncio.create_task(go()))
        else:  # closed: `level` workers loop the journey
            async def worker(w):
                i = w
                while time.perf_counter() < stop_at:
                    await one(i, time.perf_counter()); i += level
            tasks = [asyncio.create_task(worker(w)) for w in range(int(level))]
        done, pending = await asyncio.wait(tasks, timeout=duration + jcfg.get("drain_timeout_s", 120)) if tasks else (set(), set())
        for p in pending: p.cancel()
    finally:
        for t in etasks + ptasks: t.cancel()
        for s_ in samplers.values(): await s_.stop()
        # Always undo injected faults, even when the cell ended before its fault_end event fired.
        for c in ([jcfg["cleanup_cmd"]] if isinstance(jcfg.get("cleanup_cmd"), str) else jcfg.get("cleanup_cmd", [])):
            p = await asyncio.create_subprocess_shell(c); await p.wait()
    wall = time.perf_counter() - t_begin

    ok = [r for r in results if r["ok"]]
    offered = int(level * duration) if mode == "open" else len(sched) if mode == "staged" else len(results)
    reasons = {}
    for r in results:
        if not r["ok"]: reasons[r["why"]] = reasons.get(r["why"], 0) + 1
    steps = {}
    for r in ok:
        for k, v in r["steps"].items(): steps.setdefault(k, []).append(v)
    out = {"journey": jcfg["name"], "mode": mode, "level": level, "duration_s": duration, "wall_s": wall,
           "offered": offered, "completed": len(ok), "failed": len(results) - len(ok), "incomplete": offered - len(results),
           "failure_reasons": reasons,
           "e2e_from_intended_ms": summarize_latencies([(r["end"] - r["intended"]) * 1000 for r in ok]),
           "service_ms": summarize_latencies([(r["end"] - r["start"]) * 1000 for r in ok]),
           "steps_ms": {k: summarize_latencies(v) for k, v in steps.items()},
           "resources": {n: s.summary() for n, s in samplers.items()},
           "probes": {n: ({"samples": len(v[0]), "max": max(v[0]), "p50": sorted(v[0])[len(v[0]) // 2], "last": v[0][-1]}
                          if v[0] else {"samples": 0, "errors": v[1][:3]}) for n, v in probes.items()},
           "events": events_log, "invalid_reasons": []}
    for n, s in out["resources"].items():
        if s.get("declared") and not s.get("valid"): out["invalid_reasons"].append(f"resource {n}: zero samples")
    for n, p in out["probes"].items():
        if p["samples"] == 0: out["invalid_reasons"].append(f"probe {n}: zero samples")
    ev = {e["name"]: e["t"] for e in events_log}
    if "fault_start" in ev and "fault_end" in ev:
        hit = [r for r in results if ev["fault_start"] <= r["start"] <= ev["fault_end"]]
        out["fault"] = {"started_during": len(hit), "failed": sum(not r["ok"] for r in hit),
                        "time_to_drain_s": max([r["end"] for r in hit if r["ok"]], default=ev["fault_end"]) - ev["fault_end"]}
    out["post_checks"] = await _post_checks(jcfg)
    for pc in out["post_checks"]:
        if pc["rc"] != 0: out["invalid_reasons"].append(f"post-check {pc['name']} failed")
    out["valid"] = not out["invalid_reasons"]
    d = os.path.join(outdir, "journeys"); os.makedirs(d, exist_ok=True)
    tag = f"{jcfg['name']}-{mode}-{level:g}"
    _wj(os.path.join(d, tag + ".json"), out)
    with gzip.open(os.path.join(d, tag + ".journeys.jsonl.gz"), "wt") as f:
        for r in results: f.write(json.dumps(r) + "\n")
    return out

async def run_all(cfg_path, outdir, sessions=None, seed=None):
    cfg = _rj(cfg_path)
    rng = random.Random(seed if seed is not None else cfg.get("seed"))
    from . import env as envmod
    os.makedirs(outdir, exist_ok=True)
    _wj(os.path.join(outdir, "environment.json"), envmod.collect(cfg.get("environment")))
    _wj(os.path.join(outdir, "config.json"), cfg)
    a = cfg["auth"]
    auth = Auth(a["keycloak_url"], a.get("realm", "enterprise"), a.get("client_id", "enterprise-web"), a["users"], a.get("password", "dev-password"))
    await auth.init()
    try:
        for s in range(sessions or cfg.get("sessions", 5)):
            sdir = os.path.join(outdir, f"session-{s+1:02d}")
            cells = [(j, "open", r) for j in cfg["journeys"] for r in j.get("rates", [])] + \
                    [(j, "closed", c) for j in cfg["journeys"] for c in j.get("closed", [])] + \
                    [(j, "staged", 1) for j in cfg["journeys"] if j.get("staged")]
            rng.shuffle(cells)                      # order randomised per session
            for j, mode, level in cells:
                if j.get("reset_cmd"):
                    p = await asyncio.create_subprocess_shell(j["reset_cmd"]); await p.wait()
                r = await run_cell(j, cfg, auth, mode, level, sdir, j.get("duration_s", cfg.get("duration_s", 30)), rng)
                e = r["e2e_from_intended_ms"]
                print(f"s{s+1} {j['name']} {mode}={level:g}: ok {r['completed']}/{r['offered']} "
                      f"p50={e.get('p50', float('nan'))/1000:.2f}s p99={e.get('p99', float('nan'))/1000:.2f}s valid={r['valid']}", flush=True)
    finally:
        auth.stop()
