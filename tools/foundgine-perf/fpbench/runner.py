"""Session runner: interleaved, order-randomised cells across independent sessions."""
from .io import _wj, _wt, _rj
import asyncio, gzip, json, os, random, subprocess, sys, time
from . import env as envmod
from .loadgen import Target, run_phase, warm_until_stable, windows
from .sampler import Sampler
from .stats import summarize_latencies, littles_law_check

def _load(path, base):
    p = path if os.path.isabs(path) else os.path.join(base, path)
    return _rj(p)

async def _cell(name, t_cfg, scenario, mode, conc, rate, cfg, outdir):
    target = Target(t_cfg["url"], scenario, cfg.get("timeout_s", 30))
    if t_cfg.get("reset_cmd"):
        subprocess.run(t_cfg["reset_cmd"], shell=True, check=True)
    w = cfg.get("warmup", {})
    warm = await warm_until_stable(target, mode, conc, rate, w.get("min_s", 3), w.get("max_s", 30), w.get("cv", 0.05))
    sampler = Sampler(t_cfg.get("resources"), cfg.get("sample_interval_s", 1.0))
    sampler.start()
    samples, wall = await run_phase(target, mode, cfg.get("measure_s", 10), conc, rate, cfg.get("max_inflight", 256))
    await sampler.stop()
    good = [s for s in samples if s[3]]
    bad = len(samples) - len(good)
    svc = summarize_latencies([s[1] for s in good])
    # open loop: latency from intended send time; closed loop: equal to service time
    e2e = summarize_latencies([s[2] for s in good])
    res = {"target": name, "scenario": scenario["name"], "mode": mode, "concurrency": conc, "rate": rate,
           "wall_s": wall, "completed": len(good), "errors": bad,
           "throughput_rps": len(good) / wall if wall else 0,
           "logical_rps": (len(good) / wall if wall else 0) * scenario.get("ops_per_request", 1),
           "warmup": {k: warm[k] for k in ("stable", "seconds")},
           "service_ms": svc, "latency_from_intended_ms": e2e,
           "per_second": windows(samples), "resources": sampler.summary(), "valid": True, "invalid_reasons": []}
    if not warm["stable"]:
        res["invalid_reasons"].append("warmup did not reach stable throughput")
    if res["resources"].get("declared") and not res["resources"].get("valid"):
        res["invalid_reasons"].append("resource telemetry produced zero samples")
    if samples and bad / len(samples) > cfg.get("max_error_rate", 0.01):
        res["invalid_reasons"].append(f"error rate {bad/len(samples):.2%} above limit")
    if mode == "closed" and svc.get("n"):
        res["littles_law"] = littles_law_check(conc, res["throughput_rps"], svc["mean"])
        if res["littles_law"] and not res["littles_law"]["ok"]:
            res["invalid_reasons"].append("Little's Law mismatch (>15%)")
    res["valid"] = not res["invalid_reasons"]
    d = os.path.join(outdir, scenario["name"], name)
    os.makedirs(d, exist_ok=True)
    tag = f"{mode}-c{conc}" if mode == "closed" else f"open-r{int(rate)}"
    _wj(os.path.join(d, tag + ".json"), res)
    with gzip.open(os.path.join(d, tag + ".samples.jsonl.gz"), "wt") as f:
        for s in samples:
            f.write(json.dumps(s) + "\n")
    return res

async def _equivalence(cfg, scenarios, outdir):
    out = {}
    for sc in scenarios:
        probes = {n: await Target(t["url"], sc).probe() for n, t in cfg["targets"].items()}
        names = list(probes)
        ref = probes[names[0]]
        mism = []
        for n in names:
            for i, (a, b) in enumerate(zip(ref, probes[n])):
                if not b["ok"] or a["hash"] != b["hash"]:
                    mism.append({"target": n, "request": i, "ok": b["ok"], "hash_matches": a["hash"] == b["hash"]})
        out[sc["name"]] = {"equivalent": not mism, "mismatches": mism}
    _wj(os.path.join(outdir, "equivalence.json"), out)
    return out

async def run_all(cfg_path, outdir, sessions=None, seed=None):
    base = os.path.dirname(os.path.abspath(cfg_path))
    cfg = _rj(cfg_path)
    scenarios = [_load(s, base) for s in cfg["scenarios"]]
    n_sessions = sessions or cfg.get("sessions", 5)
    rng = random.Random(seed if seed is not None else cfg.get("seed"))
    os.makedirs(outdir, exist_ok=True)
    _wj(os.path.join(outdir, "environment.json"), envmod.collect(cfg.get("environment")))
    _wj(os.path.join(outdir, "config.json"), cfg)
    eq = await _equivalence(cfg, scenarios, outdir)
    for sc in scenarios:
        print(f"equivalence[{sc['name']}]: {'OK' if eq[sc['name']]['equivalent'] else 'MISMATCH'}", flush=True)
    for s in range(n_sessions):
        sdir = os.path.join(outdir, f"session-{s+1:02d}")
        for sc in scenarios:
            capacity = {}
            cells = [("closed", c, None) for c in cfg.get("closed_concurrency", [1, 8, 32])]
            for mode, conc, _ in cells:
                order = list(cfg["targets"]); rng.shuffle(order)   # randomised, interleaved per cell
                for name in order:
                    r = await _cell(name, cfg["targets"][name], sc, mode, conc, None, cfg, sdir)
                    capacity[name] = max(capacity.get(name, 0), r["throughput_rps"])
                    print(f"s{s+1} {sc['name']} {name} c={conc}: {r['throughput_rps']:.0f} rps "
                          f"p99={r['service_ms'].get('p99', float('nan')):.1f}ms valid={r['valid']}", flush=True)
            for frac in cfg.get("open_loop_fractions", []):
                order = list(cfg["targets"]); rng.shuffle(order)
                for name in order:
                    rate = max(1.0, capacity[name] * frac)
                    r = await _cell(name, cfg["targets"][name], sc, "open", 0, rate, cfg, sdir)
                    print(f"s{s+1} {sc['name']} {name} open {frac:.0%} of cap ({rate:.0f} rps): "
                          f"p99(intended)={r['latency_from_intended_ms'].get('p99', float('nan')):.1f}ms", flush=True)
    return outdir
