#!/usr/bin/env python3
"""
build-reduction-summary.py — consolidate token/agent-work reduction numbers
from every benchmark in the repo into one JSON asset for the docs site.

Why this exists
----------------
Run1-5 execute a live conventional-vs-Foundgine comparison and already
compute a *measured* EstimatedContextLoadSavingPercent/ToolCallSavingPercent
per cell (see Run1/Program.cs Comparison.Create). Those numbers are surfaced
today by the interactive matrix on docs-site/agent-benchmark/index.html,
fed by benchmark-matrix.json.

Two other benchmarks in this repo have no conventional counterpart to run
side-by-side, so they can only report a *modeled* estimate:
  - src/csharp/samples/Foundgine.SupplyChain.Advanced (agent authorization workload,
    MCP-only) -> reports/supply-chain-report.json ("efficiencyEstimate")
  - src/csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Benchmarks (in-process pipeline
    weight benchmark) -> reports/pipeline-benchmark.json ("efficiencyEstimate")

This script reads whichever of those report files it can find and merges
them into one small, clearly-labelled JSON file
(docs-site/assets/agent-benchmark/reduction-summary.json) so the website can
show "measured" and "modeled" reduction estimates side by side without
conflating them.

Usage
-----
    python3 build-reduction-summary.py [--repo-root PATH] [--out PATH]

Missing input files are skipped, not fatal — run whichever benchmarks you
have and this script will summarize what's available.
"""
import argparse
import json
import sys
from pathlib import Path


def load_json(path: Path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        return None
    except json.JSONDecodeError as e:
        print(f"warning: could not parse {path}: {e}", file=sys.stderr)
        return None


def measured_headline(repo_root: Path):
    """Best-effort pull of the published Run1-5 measured headline numbers,
    from whatever benchmark-matrix.json / per-run manifests already exist
    under docs-site/assets/agent-benchmark. Returns None if nothing has been
    published yet (e.g. this is a fresh checkout that hasn't run/published
    any benchmark)."""
    matrix_path = repo_root / "docs-site/assets/agent-benchmark/benchmark-matrix.json"
    matrix = load_json(matrix_path)
    if not matrix:
        return None
    return {
        "source": "benchmark-matrix.json",
        "note": (
            "Measured: Run1-5 execute the conventional and Foundgine flows "
            "live against the same fixture. See the interactive matrix on "
            "the Agent Benchmark page for the full workload/concurrency grid."
        ),
        "cellCount": len(matrix) if isinstance(matrix, list) else None,
    }


def supply_chain_summary(repo_root: Path):
    for candidate in [
        repo_root / "src/csharp/samples/Foundgine.SupplyChain.Advanced/reports/supply-chain-report.json",
        repo_root / "docs-site/assets/agent-benchmark/supply-chain/supply-chain-report.json",
    ]:
        report = load_json(candidate)
        if report and "efficiencyEstimate" in report:
            est = report["efficiencyEstimate"]
            modeled_conventional = est.get("modeledConventional") or {}
            measured_foundgine = est.get("measuredFoundgine") or {}
            return {
                "benchmark": "Supply Chain E2E (agent authorization workload)",
                "kind": "modeled",
                "sourceFile": str(candidate.relative_to(repo_root)),
                "method": est.get("method"),
                "estimatedToolCallReductionPercent": est.get("modeledToolCallReductionPercent") or est.get("estimatedToolCallReductionPercent"),
                "estimatedContextLoadReductionPercent": est.get("modeledContextLoadReductionPercent") or est.get("estimatedContextLoadReductionPercent"),
                # "How many round trips would this take without Foundgine?"
                # One MCP round trip per successful capability call versus a
                # modeled discover/authorize/execute/verify choreography.
                "measuredFoundgineRoundTrips": measured_foundgine.get("roundTrips") or measured_foundgine.get("toolCalls"),
                "modeledConventionalRoundTrips": modeled_conventional.get("estimatedRoundTrips") or modeled_conventional.get("estimatedToolCalls"),
                "roundTripsPerCapability": {
                    "foundgine": 1,
                    "modeledConventional": modeled_conventional.get("roundTripsPerCapability") or modeled_conventional.get("stepsPerCapabilityMultiplier"),
                },
                "measuredFoundgine": measured_foundgine,
                "modeledConventional": modeled_conventional,
                "caveats": est.get("caveats"),
            }
    return None


def semantic_pipeline_summary(repo_root: Path):
    for candidate in [
        repo_root / "src/csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Benchmarks/reports/pipeline-benchmark.json",
        repo_root / "docs-site/assets/agent-benchmark/semantic-pipeline/pipeline-benchmark.json",
    ]:
        report = load_json(candidate)
        if report and "efficiencyEstimate" in report:
            est = report["efficiencyEstimate"]
            return {
                "benchmark": "SupplyChain.Semantic pipeline-weight benchmark",
                "kind": "modeled",
                "sourceFile": str(candidate.relative_to(repo_root)),
                "method": est.get("method") or est.get("Method"),
                "scenarios": est.get("scenarios") or est.get("Scenarios"),
            }
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repo-root", default=None, help="Repo root (defaults to three levels up from this script)")
    parser.add_argument("--out", default=None, help="Output path (defaults to docs-site/assets/agent-benchmark/reduction-summary.json)")
    args = parser.parse_args()

    script_dir = Path(__file__).resolve().parent
    repo_root = Path(args.repo_root).resolve() if args.repo_root else (script_dir / "../../..").resolve()
    out_path = Path(args.out).resolve() if args.out else repo_root / "docs-site/assets/agent-benchmark/reduction-summary.json"

    summary = {
        "schemaVersion": 1,
        "note": (
            "Two kinds of numbers appear here. 'measured' comes from a live "
            "conventional-vs-Foundgine run against the same fixture (see "
            "Run1-5). 'modeled' comes from a benchmark that has no "
            "conventional counterpart to run side by side, so the "
            "conventional side is estimated from documented assumptions "
            "instead of executed. Never present a 'modeled' number as a "
            "measured result."
        ),
        "measured": measured_headline(repo_root),
        "modeled": [
            x for x in [supply_chain_summary(repo_root), semantic_pipeline_summary(repo_root)] if x is not None
        ],
    }

    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    found = 1 + len(summary["modeled"]) if summary["measured"] else len(summary["modeled"])
    print(f"Wrote {out_path} ({found} source(s) found: "
          f"measured={'yes' if summary['measured'] else 'no'}, modeled={len(summary['modeled'])})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
