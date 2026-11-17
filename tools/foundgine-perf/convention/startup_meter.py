#!/usr/bin/env python3
"""Cold-start + memory meter for JIT vs Native AOT (or any two builds).

usage: startup_meter.py --name aot --cmd "./bin/api" --url http://127.0.0.1:5000/health \
          [--reps 20] [--settle 3] [--env K=V ...] [--binary ./bin/api] [--json out.json]

Per repetition (fresh process each time): time from spawn to first HTTP 2xx; RSS right
after readiness and after --settle seconds; the process is then killed. Binary/publish
size is recorded once. Report medians with min/max; compare builds with report.py's
bootstrap if you collect >= 5 sessions (run this inside each session).
"""
import argparse, json, os, shlex, statistics, subprocess, time, urllib.request

def rss(pid):
    try:
        for l in open(f"/proc/{pid}/status"):
            if l.startswith("VmRSS"): return int(l.split()[1]) / 1024
    except OSError: pass
    return None

def once(cmd, url, env, settle, timeout=60):
    t0 = time.perf_counter()
    p = subprocess.Popen(shlex.split(cmd), env={**os.environ, **env}, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        while time.perf_counter() - t0 < timeout:
            if p.poll() is not None: return {"error": f"exited {p.returncode}"}
            try:
                with urllib.request.urlopen(url, timeout=1) as r:
                    if 200 <= r.status < 300: break
            except Exception: time.sleep(0.005)
        else:
            return {"error": "timeout"}
        ready = (time.perf_counter() - t0) * 1000
        r0 = rss(p.pid); time.sleep(settle)
        return {"ready_ms": ready, "rss_ready_mb": r0, "rss_settled_mb": rss(p.pid)}
    finally:
        p.kill(); p.wait()

def main():
    a = argparse.ArgumentParser()
    a.add_argument("--name", required=True); a.add_argument("--cmd", required=True); a.add_argument("--url", required=True)
    a.add_argument("--reps", type=int, default=20); a.add_argument("--settle", type=float, default=3)
    a.add_argument("--env", nargs="*", default=[]); a.add_argument("--binary"); a.add_argument("--json")
    a = a.parse_args()
    env = dict(e.split("=", 1) for e in a.env)
    runs = [once(a.cmd, a.url, env, a.settle) for _ in range(a.reps)]
    ok = [r for r in runs if "error" not in r]
    med = lambda k: statistics.median(r[k] for r in ok if r.get(k) is not None) if ok else None
    out = {"name": a.name, "reps": a.reps, "ok": len(ok), "failures": [r["error"] for r in runs if "error" in r][:3],
           "ready_ms_median": med("ready_ms"), "ready_ms_min": min((r["ready_ms"] for r in ok), default=None),
           "ready_ms_max": max((r["ready_ms"] for r in ok), default=None),
           "rss_ready_mb_median": med("rss_ready_mb"), "rss_settled_mb_median": med("rss_settled_mb"),
           "binary_mb": round(os.path.getsize(a.binary) / 1e6, 2) if a.binary and os.path.exists(a.binary) else None}
    print(json.dumps(out, indent=2))
    if a.json: json.dump(out, open(a.json, "w"), indent=2)
    if not ok: raise SystemExit("no successful runs")
main()
