"""Async load generator, stdlib only.

closed loop : N workers, each sends the next request when the previous returns.
              Latency = response time (service time). Subject to coordinated omission.
open loop   : requests are scheduled at a fixed rate from a start time. Latency is
              measured from the INTENDED send time (Tene), so queueing delay under
              overload is counted rather than hidden.

Every response is validated and canonically hashed so targets can be compared for
response equivalence (the report's action 2).
"""
import asyncio, hashlib, json, time
from urllib.parse import urlsplit

class Conn:
    def __init__(self, host, port):
        self.host, self.port, self.r, self.w = host, port, None, None

    async def _open(self):
        self.r, self.w = await asyncio.open_connection(self.host, self.port)

    async def request(self, method, path, body, headers, timeout):
        for attempt in (0, 1):  # one transparent reconnect for stale keep-alive sockets
            try:
                if self.w is None or self.w.is_closing():
                    await self._open()
                data = body.encode() if isinstance(body, str) else (body or b"")
                hdrs = {"Host": f"{self.host}:{self.port}", "Content-Length": str(len(data)),
                        "Connection": "keep-alive", **headers}
                head = f"{method} {path} HTTP/1.1\r\n" + "".join(f"{k}: {v}\r\n" for k, v in hdrs.items()) + "\r\n"
                self.w.write(head.encode() + data)
                await self.w.drain()
                return await asyncio.wait_for(self._read(), timeout)
            except (ConnectionError, asyncio.IncompleteReadError):
                self.close()
                if attempt:
                    raise

    async def _read(self):
        status_line = await self.r.readline()
        if not status_line:
            raise ConnectionError("closed")
        status = int(status_line.split()[1])
        clen, chunked = None, False
        while True:
            line = await self.r.readline()
            if line in (b"\r\n", b"\n", b""):
                break
            k, _, v = line.decode().partition(":")
            k = k.strip().lower()
            if k == "content-length":
                clen = int(v)
            elif k == "transfer-encoding" and "chunked" in v.lower():
                chunked = True
        if chunked:
            parts = []
            while True:
                size = int((await self.r.readline()).strip() or b"0", 16)
                if size == 0:
                    await self.r.readline()
                    break
                parts.append(await self.r.readexactly(size))
                await self.r.readline()
            payload = b"".join(parts)
        else:
            payload = await self.r.readexactly(clen) if clen else b""
        return status, payload

    def close(self):
        if self.w:
            try: self.w.close()
            except Exception: pass
        self.r = self.w = None

def _drop(obj, keys):
    if isinstance(obj, dict):
        return {k: _drop(v, keys) for k, v in obj.items() if k not in keys}
    if isinstance(obj, list):
        return [_drop(v, keys) for v in obj]
    return obj

def canonical(payload, ignore_keys=(), sort_lists_by=None):
    """Return (sha256, row_count) of the normalised JSON body."""
    try:
        doc = json.loads(payload)
    except ValueError:
        return hashlib.sha256(payload).hexdigest(), None
    doc = _drop(doc, set(ignore_keys))
    if sort_lists_by:
        def srt(o):
            if isinstance(o, list):
                o = [srt(x) for x in o]
                if o and isinstance(o[0], dict) and sort_lists_by in o[0]:
                    o.sort(key=lambda d: str(d.get(sort_lists_by)))
                return o
            if isinstance(o, dict):
                return {k: srt(v) for k, v in o.items()}
            return o
        doc = srt(doc)
    flat = json.dumps(doc, sort_keys=True, separators=(",", ":"))
    count = flat.count('"') // 2  # cheap structural size proxy, comparable across targets
    return hashlib.sha256(flat.encode()).hexdigest(), count

class Target:
    def __init__(self, url, scenario, timeout=30.0, tokens=None):
        u = urlsplit(url)
        self.tokens = tokens
        self.host, self.port = u.hostname, u.port or 80
        self.base = u.path.rstrip("/")
        self.s, self.timeout = scenario, timeout
        self.errors_text = scenario.get("error_markers", ["\"errors\""])

    def build(self, i):
        reqs = self.s["requests"]
        r = reqs[i % len(reqs)]
        body = r.get("body")
        if isinstance(body, (dict, list)):
            body = json.dumps(body)
        hdrs = {"Content-Type": "application/json", **r.get("headers", {})}
        if r.get("as"):
            if self.tokens is None:
                raise RuntimeError(f"scenario {self.s.get('name')} requests identity {r['as']!r} but the config has no `auth` block")
            hdrs["Authorization"] = "Bearer " + self.tokens.get(r["as"])
        return r.get("method", "GET"), self.base + r["path"], body, hdrs

    def ok(self, status, payload):
        if not 200 <= status < 300:
            return False
        txt = payload[:65536].decode("utf-8", "ignore")
        return not any(m in txt for m in self.errors_text)

    async def probe(self):
        c = Conn(self.host, self.port)
        out = []
        for i in range(len(self.s["requests"])):
            m, p, b, h = self.build(i)
            status, payload = await c.request(m, p, b, h, self.timeout)
            digest, count = canonical(payload, self.s.get("ignore_keys", []), self.s.get("sort_lists_by"))
            out.append({"status": status, "ok": self.ok(status, payload), "hash": digest, "size": count})
        c.close()
        return out

async def _closed(t, conc, duration, record):
    stop = time.perf_counter() + duration
    async def worker(w):
        c, i = Conn(t.host, t.port), w
        while time.perf_counter() < stop:
            m, p, b, h = t.build(i); i += conc
            t0 = time.perf_counter()
            try:
                st, pl = await c.request(m, p, b, h, t.timeout)
                ok = t.ok(st, pl)
            except Exception as e:
                ok, st = False, type(e).__name__
            t1 = time.perf_counter()
            record(t0, t1, t0, ok, st)
        c.close()
    await asyncio.gather(*(worker(w) for w in range(conc)))

async def _open_loop(t, rate, duration, max_inflight, record):
    pool = asyncio.Queue()
    for _ in range(max_inflight):
        pool.put_nowait(Conn(t.host, t.port))
    start = time.perf_counter()
    n = int(rate * duration)
    tasks = []
    async def fire(i, intended):
        c = await pool.get()          # waiting here is queueing delay: it IS counted
        m, p, b, h = t.build(i)
        t0 = time.perf_counter()
        try:
            st, pl = await c.request(m, p, b, h, t.timeout)
            ok = t.ok(st, pl)
        except Exception as e:
            ok, st = False, type(e).__name__
            c.close()
        t1 = time.perf_counter()
        pool.put_nowait(c)
        record(t0, t1, intended, ok, st)
    for i in range(n):
        intended = start + i / rate
        delay = intended - time.perf_counter()
        if delay > 0:
            await asyncio.sleep(delay)
        tasks.append(asyncio.create_task(fire(i, intended)))
    await asyncio.gather(*tasks)
    while not pool.empty():
        pool.get_nowait().close()

async def run_phase(target, mode, duration, concurrency=1, rate=None, max_inflight=256):
    """Returns per-window throughput and raw samples (relative seconds)."""
    samples = []
    t_begin = time.perf_counter()
    def record(t0, t1, intended, ok, st):
        samples.append((t0 - t_begin, (t1 - t0) * 1000.0, (t1 - intended) * 1000.0, ok, st))
    if mode == "closed":
        await _closed(target, concurrency, duration, record)
    else:
        await _open_loop(target, rate, duration, max_inflight, record)
    return samples, time.perf_counter() - t_begin

def windows(samples, width=1.0):
    from collections import Counter
    c = Counter(int((s[0] + s[1] / 1000.0) // width) for s in samples if s[3])
    return [c[k] / width for k in sorted(c)]

async def warm_until_stable(target, mode, conc, rate, min_s, max_s, cv_limit, need=3):
    """Warm up until throughput over 1s windows is stable (not a fixed 3 s)."""
    from .stats import cv
    elapsed, history = 0.0, []
    while elapsed < max_s:
        s, d = await run_phase(target, mode, 1.0, conc, rate)
        elapsed += d
        history.append(len(s) / d)
        if elapsed >= min_s and len(history) >= need and cv(history[-need:]) <= cv_limit:
            return {"stable": True, "seconds": elapsed, "history": history}
    return {"stable": False, "seconds": elapsed, "history": history}
