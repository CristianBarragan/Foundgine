"""Statistics: nearest-rank percentiles, bootstrap CIs across sessions, Little's Law."""
import math, random, statistics

def percentile(sorted_vals, p):
    """Nearest-rank percentile on an already sorted list (p in 0..100)."""
    if not sorted_vals:
        return float("nan")
    k = max(1, math.ceil(p / 100.0 * len(sorted_vals)))
    return sorted_vals[k - 1]

def summarize_latencies(lat_ms):
    s = sorted(lat_ms)
    if not s:
        return {"n": 0}
    return {"n": len(s), "mean": sum(s) / len(s), "p50": percentile(s, 50),
            "p90": percentile(s, 90), "p95": percentile(s, 95),
            "p99": percentile(s, 99), "p999": percentile(s, 99.9), "max": s[-1]}

def bootstrap_ratio_ci(a, b, stat=statistics.median, n=5000, seed=42, alpha=0.05):
    """Ratio stat(a)/stat(b) with a percentile bootstrap CI.
    a and b are per-session values (independent sessions), NOT per-request samples."""
    rng = random.Random(seed)
    if not a or not b:
        return None
    point = stat(a) / stat(b)
    ratios = []
    for _ in range(n):
        ra = [rng.choice(a) for _ in a]
        rb = [rng.choice(b) for _ in b]
        d = stat(rb)
        if d:
            ratios.append(stat(ra) / d)
    ratios.sort()
    lo = ratios[int(alpha / 2 * len(ratios))]
    hi = ratios[min(len(ratios) - 1, int((1 - alpha / 2) * len(ratios)))]
    return {"ratio": point, "lo": lo, "hi": hi, "sessions_a": len(a), "sessions_b": len(b),
            "excludes_1": lo > 1 or hi < 1}

def littles_law_check(concurrency, throughput_rps, mean_latency_ms, tol=0.15):
    """L = lambda * W. Only meaningful for closed loop."""
    if throughput_rps <= 0:
        return None
    implied = concurrency / throughput_rps * 1000.0
    err = abs(implied - mean_latency_ms) / implied
    return {"implied_mean_ms": implied, "rel_error": err, "ok": err <= tol}

def cv(vals):
    if len(vals) < 2:
        return float("inf")
    m = statistics.mean(vals)
    return statistics.pstdev(vals) / m if m else float("inf")
