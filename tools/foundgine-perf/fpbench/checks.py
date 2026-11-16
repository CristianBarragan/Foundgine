"""Evaluators that turn raw harness output into readiness evidence. Each writes one evidence record (pass/fail/not_run)."""
import glob, gzip, json, math, os, resource, statistics, subprocess, sys, tempfile
from . import readiness
from .io import _rj, _wj
from .stats import percentile

def _cells(results):
    out = []
    for f in sorted(glob.glob(os.path.join(results, "session-*", "journeys", "*.json"))):
        c = _rj(f); c["_file"] = f; out.append(c)
    return out

def aa(report_path, evdir, min_frac=0.90, max_dev=0.10, min_sessions=5, cid="R1"):
    """A/A test: the same system as baseline and subject must look the same. Ratios within +-max_dev and the 95% CI
    containing 1.0 in >= min_frac of cells (a 95% interval misses in ~5% of cells by construction, so 'every cell' would flake)."""
    r = _rj(report_path); rows = [x for x in r["rows"] if x.get("ci")]
    if not rows or r["sessions"] < min_sessions:
        return readiness.record(evdir, cid, "not_run", detail=f"need >= {min_sessions} sessions and cells with intervals (have {r['sessions']} sessions, {len(rows)} cells)")
    inside = sum(not x["ci"]["excludes_1"] for x in rows); dev = max(abs(x["ci"]["ratio"] - 1) for x in rows)
    ok = inside / len(rows) >= min_frac and dev <= max_dev and not any(x["equivalent"] is False for x in r["rows"])
    return readiness.record(evdir, cid, "pass" if ok else "fail", {"cells": len(rows), "ci_contains_1": inside, "max_ratio_deviation": round(dev, 3)},
                            f"CI contains 1 in >= {min_frac:.0%} of cells and |ratio-1| <= {max_dev:.0%}", artifacts=[report_path])

def _bucket_p99(jsonl_gz, wall_s, buckets=10):
    by = [[] for _ in range(buckets)]
    with gzip.open(jsonl_gz, "rt") as f:
        for line in f:
            x = json.loads(line)
            if x["ok"]: by[min(buckets - 1, int(x["intended"] / wall_s * buckets))].append((x["end"] - x["intended"]) * 1000)
    return [percentile(b, 99) if len(b) >= 20 else None for b in by]

def soak(results, evdir, min_hours=24.0, max_rss_growth=0.05, max_p99_drift=0.10, max_fail=0.01, cid="R11"):
    cells = _cells(results)
    if not cells: return readiness.record(evdir, cid, "not_run", detail=f"no journey cells under {results}")
    c = max(cells, key=lambda x: x["duration_s"])
    if c["duration_s"] < min_hours * 3600:
        return readiness.record(evdir, cid, "not_run", {"duration_h": round(c["duration_s"] / 3600, 2)}, f">= {min_hours} h", detail="soak too short to count")
    problems, m = [], {"duration_h": round(c["duration_s"] / 3600, 2), "rss_growth": {}}
    if not c["valid"]: problems.append("cell invalid: " + "; ".join(c["invalid_reasons"]))
    fail = (c["failed"] + c["incomplete"]) / max(1, c["offered"]); m["failure_rate"] = round(fail, 4)
    if fail > max_fail: problems.append(f"failure rate {fail:.2%}")
    for name, r in c["resources"].items():
        s = r.get("rss_mb_series")
        if not s or len(s) < 20: problems.append(f"{name}: no RSS series"); continue
        n = len(s); early = statistics.mean(s[n // 10: n // 5]); late = statistics.mean(s[-n // 10:])
        g = late / early - 1 if early else float("inf"); m["rss_growth"][name] = round(g, 4)
        if g > max_rss_growth: problems.append(f"{name} RSS grew {g:.1%}")
    gz = c["_file"].replace(".json", ".journeys.jsonl.gz")
    if os.path.exists(gz):
        b = _bucket_p99(gz, c["wall_s"]); e, l = b[1], b[-1]
        if e and l:
            d = l / e - 1; m["p99_drift"] = round(d, 4)
            if d > max_p99_drift: problems.append(f"p99 drifted {d:.1%}")
        else: problems.append("not enough completions per window for a p99 trend")
    return readiness.record(evdir, cid, "fail" if problems else "pass", m,
                            f"RSS growth <= {max_rss_growth:.0%}, p99 drift <= {max_p99_drift:.0%}, failures <= {max_fail:.0%}", detail="; ".join(problems), artifacts=[results])

def faults(results, evdir, cid, prefix, require, max_drain_s=None, require_valid_checks=True):
    """Fault-matrix evaluator. Cells are journeys named <prefix><target>; each must have run, passed all post-checks
    (data invariants) and be valid. `require` lists the targets that must be present, so a missing cell is not_run, not pass."""
    cells = {c["journey"]: c for c in _cells(results)}   # last session wins per name; every session is checked below
    allc = _cells(results); problems, seen = [], set()
    for c in allc:
        if not c["journey"].startswith(prefix): continue
        seen.add(c["journey"][len(prefix):])
        if not c["valid"]: problems.append(f"{c['journey']} ({os.path.basename(os.path.dirname(os.path.dirname(c['_file'])))}): " + "; ".join(c["invalid_reasons"]))
        if require_valid_checks and not c.get("post_checks"): problems.append(f"{c['journey']}: no data-invariant post-checks recorded")
        if max_drain_s is not None and c.get("fault", {}).get("time_to_drain_s", 0) > max_drain_s:
            problems.append(f"{c['journey']}: drain {c['fault']['time_to_drain_s']:.0f}s > {max_drain_s}s")
    missing = sorted(set(require) - seen)
    if missing: return readiness.record(evdir, cid, "not_run", {"present": sorted(seen)}, detail="missing cells: " + ", ".join(missing))
    return readiness.record(evdir, cid, "fail" if problems else "pass", {"cells": len(seen)}, "all cells valid, zero invariant violations", "; ".join(problems[:5]), artifacts=[results])

def tenant(results, evdir, min_journeys=1000, cid="R12"):
    cells = [c for c in _cells(results) if c["journey"].startswith("tenant-isolation")]
    if not cells: return readiness.record(evdir, cid, "not_run", detail="no tenant-isolation cells")
    done = sum(c["completed"] for c in cells); leaks = sum(n for c in cells for k, n in c["failure_reasons"].items() if k.startswith("LEAK"))
    other = sum(c["failed"] + c["incomplete"] for c in cells) - leaks
    m = {"journeys_completed": done, "leaks": leaks, "other_failures": other}
    if leaks: return readiness.record(evdir, cid, "fail", m, "0 leaks", f"{leaks} cross-tenant reads succeeded", artifacts=[results])
    if done < min_journeys: return readiness.record(evdir, cid, "not_run", m, f">= {min_journeys} journeys", "too few completed journeys to claim isolation")
    if other > 0.05 * (done + other): return readiness.record(evdir, cid, "fail", m, "other failures <= 5% (otherwise the test may pass vacuously)", "too many non-leak failures", artifacts=[results])
    return readiness.record(evdir, cid, "pass", m, f"0 leaks over >= {min_journeys} journeys with positive controls", artifacts=[results])

def calibrate(config, evdir, rate, duration=30, max_cpu=0.70, min_completion=0.98, cid="R3"):
    """Loader calibration: run the journey loader at the highest planned rate against a NULL stack (mock or stub) and
    measure how much of one core the loader itself needs. If it is near 1.0 the loader, not the system, is the limit."""
    cfg = _rj(config)
    cfg["sessions"], cfg["duration_s"] = 1, duration
    for j in cfg["journeys"]: j["rates"], j["closed"], j["events"] = [rate], [], []
    cfg["journeys"] = cfg["journeys"][:1]
    d = tempfile.mkdtemp(); p = os.path.join(d, "cal.json"); _wj(p, cfg)
    before = resource.getrusage(resource.RUSAGE_CHILDREN); import time; t0 = time.time()
    r = subprocess.run([sys.executable, "-m", "fpbench", "journeys", p, "--out", os.path.join(d, "out")], capture_output=True, text=True)
    wall = time.time() - t0; after = resource.getrusage(resource.RUSAGE_CHILDREN)
    cpu = ((after.ru_utime - before.ru_utime) + (after.ru_stime - before.ru_stime)) / wall
    cells = _cells(os.path.join(d, "out"))
    comp = cells[0]["completed"] / max(1, cells[0]["offered"]) if cells else 0
    ok = r.returncode == 0 and cpu <= max_cpu and comp >= min_completion
    return readiness.record(evdir, cid, "pass" if ok else "fail", {"loader_cpu_cores": round(cpu, 3), "rate": rate, "completion": round(comp, 3)},
                            f"loader <= {max_cpu:.0%} of one core at {rate}/s with >= {min_completion:.0%} completion", detail=("" if ok else (r.stderr[-300:] or "over threshold")))

def perf(results, evdir, slo_p99_s, target_rate, journey="customer-to-cash", cid="R4"):
    """Target workload sustained within the SLO: highest sustainable open-loop rate >= target_rate and the journey report is claim-grade."""
    from . import journey_report
    rep = journey_report.build(results, slo_p99_s)
    got = rep["sustainable_rate"].get(journey, 0)
    ok = got >= target_rate and rep["claim_grade"]
    return readiness.record(evdir, cid, "pass" if ok else "fail", {"sustainable_rate": got, "target_rate": target_rate, "slo_p99_s": slo_p99_s, "claim_grade": rep["claim_grade"]},
                            f"{journey} sustains >= {target_rate}/s with p99 <= {slo_p99_s}s, failures <= 1%, completion >= 98%, claim-grade", "; ".join(rep["blockers"]), artifacts=[results])

def claim(report_path, evdir, cid="R5"):
    """Evidence quality of the engine comparison (not who wins): claim-grade report, every cell equivalent."""
    r = _rj(report_path); ok = r["claim_grade"] and all(x["equivalent"] for x in r["rows"])
    return readiness.record(evdir, cid, "pass" if ok else "fail", {"sessions": r["sessions"], "cells": len(r["rows"])},
                            "claim grade YES, 5+ sessions, equivalent responses, telemetry, disclosed environment", "; ".join(r["blockers"]), artifacts=[report_path])

def restart(results, evdir, max_error_rate=0.001, prefix="graceful-restart-", cid="R17"):
    """Graceful restart under load: failed+incomplete journeys over offered, plus data invariants after the restart.
    With a single instance this measures the outage window, so a fail here is an honest finding, not a harness error."""
    cells = [c for c in _cells(results) if c["journey"].startswith(prefix)]
    if not cells: return readiness.record(evdir, cid, "not_run", detail=f"no {prefix}* cells under {results}")
    off = sum(c["offered"] for c in cells); bad = sum(c["failed"] + c["incomplete"] for c in cells); rate = bad / max(1, off)
    inv = [c["journey"] for c in cells if not c.get("post_checks") or any(p["rc"] for p in c["post_checks"])]
    m = {"offered": off, "failed": bad, "error_rate": round(rate, 4), "cells": len(cells)}
    why = ([f"error rate {rate:.2%} > {max_error_rate:.2%}"] if rate > max_error_rate else []) + ([f"invariants failed/missing in: {', '.join(inv)}"] if inv else [])
    return readiness.record(evdir, cid, "fail" if why else "pass", m, f"error rate < {max_error_rate:.2%} and zero invariant violations", "; ".join(why), artifacts=[results])

def recovery(results, evdir, slo_p99_s, max_recovery_s, journey="customer-to-cash-staged", window_s=10, cid="R6"):
    """Overload then recovery. The staged cell steps the rate (normal -> overload -> normal). Recovery time = seconds after the
    overload segment ends until p99 (from intended time, per window) is back under the SLO and stays there to the end of the cell.
    Graceful degradation = the system answered (completion >= 90%) rather than collapsing."""
    cells = [c for c in _cells(results) if c["journey"] == journey]
    if not cells: return readiness.record(evdir, cid, "not_run", detail=f"no {journey} cells under {results}")
    cfg = _rj(os.path.join(results, "config.json")); segs = next(j for j in cfg["journeys"] if j["name"] == journey)["staged"]
    over_end = sum(s["s"] for s in segs[:2]); problems, m = [], {"recovery_s": []}
    for c in cells:
        comp = c["completed"] / max(1, c["offered"]); gz = c["_file"].replace(".json", ".journeys.jsonl.gz")
        if comp < 0.90: problems.append(f"completion {comp:.0%} < 90% (collapse, not degradation)")
        n = int(c["wall_s"] // window_s) + 1; by = [[] for _ in range(n)]
        with gzip.open(gz, "rt") as f:
            for line in f:
                x = json.loads(line)
                if x["ok"]: by[min(n - 1, int(x["end"] // window_s))].append((x["end"] - x["intended"]) * 1000)
        p99 = [percentile(b, 99) / 1000 if len(b) >= 5 else None for b in by]
        first = int(over_end // window_s) + 1; rec_t = None
        for i in range(first, n):
            tail = [v for v in p99[i:] if v is not None]
            if tail and all(v <= slo_p99_s for v in tail): rec_t = max(0.0, i * window_s - over_end); break
        m["recovery_s"].append(None if rec_t is None else round(rec_t, 1))
        if rec_t is None: problems.append("p99 never returned under the SLO")
        elif rec_t > max_recovery_s: problems.append(f"recovery {rec_t:.0f}s > {max_recovery_s:.0f}s")
    return readiness.record(evdir, cid, "fail" if problems else "pass", m, f"completion >= 90% and p99 <= {slo_p99_s}s within {max_recovery_s}s after overload", "; ".join(problems), artifacts=[results])
