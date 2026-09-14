#!/usr/bin/env python3
"""
estimate_tokens.py — offline token-load estimator for Foundgine agent-benchmark reports.

Why this exists
----------------
The AgentEndToEnd benchmark's `replay` mode never calls a model, so
InputTokens/OutputTokens/TotalTokens in agent-benchmark.json are always 0
(see Program.cs: "Replay intentionally has no model token counts").
That's correct behaviour, not a bug — but it means replay-mode runs give you
no signal at all about the token cost difference between the two flows.

This script fills that gap WITHOUT calling a model or a real tokenizer
service. It reads the tool Input/Output payloads that TraceCollector already
records for every run (even in replay mode) and estimates how many tokens
those payloads would cost if they were sent to/from an LLM as tool
input/output. It is intentionally conservative and clearly labelled as an
estimate — never present it as provider-reported usage.

Estimation method
------------------
We use the standard order-of-magnitude heuristic:
    tokens ≈ max(chars / 4, words * 1.3)
This tracks real BPE tokenizers (cl100k_base, o200k_base, Claude's
tokenizer, etc.) within roughly +/-15% for JSON/English payloads of the
size seen in this benchmark. It is NOT a substitute for a real tokenizer
count — if you need exact numbers, run the harness in `live` mode against
a real model endpoint, which records provider-reported usage.

Usage
-----
    python3 estimate_tokens.py agent-benchmark.json
    python3 estimate_tokens.py agent-benchmark.json --json out.json

Output
------
Prints a per-flow, per-step token breakdown for the first measured run of
each flow, plus an aggregate estimate across all measured runs. With
--json, also writes an augmented report containing EstimatedInputTokens /
EstimatedOutputTokens / EstimatedTotalTokens per run and in aggregate,
alongside the original (provider-reported, possibly zero) fields.
"""
import argparse
import json
import sys


SYSTEM_PROMPTS = {
    "Conventional application/AI flow": (
        "You are a careful banking application agent. Use the available "
        "application tools to complete the request. You must inspect the "
        "schema before querying unfamiliar data, never invent fields, and "
        "verify the final state after a mutation."
    ),
    "Foundgine semantic flow": (
        "You are a careful banking agent using Foundgine. Treat the "
        "semantic capability and graph/mutation tools as the authoritative "
        "domain interface. Do not request raw SQL or physical schema "
        "details. Verify the final state after a mutation."
    ),
}

SCENARIO_REQUEST = (
    "Review Customer 1 in the benchmark fixture. Traverse the customer's "
    "banking relationships, contracts and transactions and calculate total "
    "exposure as the sum of transaction Balance values. If exposure is at "
    "least 48,000, mark the customer as reviewed by setting FullName to "
    "exactly `Customer 1 Benchmark | Reviewed`. Then verify the final "
    "state. Return customer key, relationship count, contract count, "
    "transaction count, exposure and final full name. Do not modify any "
    "other customer or business data."
)


def estimate_tokens(text: str | None) -> int:
    """chars/4 vs words*1.3 blended heuristic — see module docstring."""
    if not text:
        return 0
    chars = len(text)
    words = len(text.split())
    return round(max(chars / 4, words * 1.3))


def analyze_run(run: dict, system_prompt_tokens: int, request_tokens: int) -> dict:
    trace = run.get("Trace") or []
    steps = []
    tool_input_tokens = 0
    tool_output_tokens = 0
    for ev in trace:
        it = estimate_tokens(ev.get("Input"))
        ot = estimate_tokens(ev.get("Output"))
        tool_input_tokens += it
        tool_output_tokens += ot
        steps.append({"name": ev.get("Name"), "kind": ev.get("Kind"), "input_tokens": it, "output_tokens": ot})

    # Rough context-load model: everything that would sit in the model's
    # context window over one full pass — system + user request (paid once,
    # as input) plus every tool call's input (goes out as part of a
    # tool_use turn) and every tool result (comes back as input on the next
    # turn). We report them separately so readers can see the split.
    estimated_input_tokens = system_prompt_tokens + request_tokens + tool_input_tokens + tool_output_tokens
    estimated_output_tokens = 0  # the harness doesn't capture model text/reasoning output at all (see caveats)

    return {
        "run": run.get("Run"),
        "steps": steps,
        "tool_input_tokens": tool_input_tokens,
        "tool_output_tokens": tool_output_tokens,
        "estimated_context_load_tokens": estimated_input_tokens,
        "estimated_output_tokens_note": (
            "Not estimated — the harness records only tool payloads, not "
            "the model's own reasoning/response text, which a live run "
            "would add on top of this."
        ),
    }


def analyze_flow(flow: dict) -> dict:
    title = flow.get("title") or flow.get("id")
    system_prompt = SYSTEM_PROMPTS.get(title, "")
    system_tokens = estimate_tokens(system_prompt)
    request_tokens = estimate_tokens(SCENARIO_REQUEST)

    runs = flow.get("runs") or []
    run_analyses = [analyze_run(r, system_tokens, request_tokens) for r in runs]
    if run_analyses:
        avg_context = sum(r["estimated_context_load_tokens"] for r in run_analyses) / len(run_analyses)
        avg_tool_in = sum(r["tool_input_tokens"] for r in run_analyses) / len(run_analyses)
        avg_tool_out = sum(r["tool_output_tokens"] for r in run_analyses) / len(run_analyses)
    else:
        avg_context = avg_tool_in = avg_tool_out = 0.0

    return {
        "flow": title,
        "system_prompt_tokens": system_tokens,
        "request_tokens": request_tokens,
        "runs": run_analyses,
        "avg_tool_input_tokens": round(avg_tool_in, 1),
        "avg_tool_output_tokens": round(avg_tool_out, 1),
        "avg_estimated_context_load_tokens": round(avg_context, 1),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("report", help="Path to agent-benchmark.json")
    parser.add_argument("--json", help="Optional path to write an augmented JSON report", default=None)
    parser.add_argument("--run", type=int, default=1, help="Which measured run to show a step-by-step breakdown for (default: 1)")
    args = parser.parse_args()

    with open(args.report, "r", encoding="utf-8") as f:
        report = json.load(f)

    flows = report.get("Flows")
    if not flows:
        # Fall back to the raw harness shape: {"Results": [ {Flow, Run, Trace, ...}, ... ]}
        results = report.get("Results")
        if not results:
            print("No 'Flows' or 'Results' array with trace data found in this report.", file=sys.stderr)
            print("Re-run the benchmark and make sure per-run Trace data is included (record=true).", file=sys.stderr)
            return 1
        grouped: dict[str, list] = {}
        for r in results:
            grouped.setdefault(r.get("Flow"), []).append(r)
        flows = [{"id": name, "title": name, "runs": runs} for name, runs in grouped.items()]

    if not any((fl.get("runs") or [None])[0] and (fl.get("runs") or [{}])[0].get("Trace") for fl in flows):
        print("Report has no per-run Trace payloads (record=false runs only).", file=sys.stderr)
        print("Re-run with at least one recorded run per flow.", file=sys.stderr)
        return 1

    analyses = [analyze_flow(fl) for fl in flows]

    print("=" * 70)
    print(" Estimated token load (offline heuristic — not provider-reported)")
    print("=" * 70)
    print("Method: tokens ~= max(chars/4, words*1.3), applied to every tool")
    print("Input/Output payload captured in the trace, plus the fixed system")
    print("prompt and scenario request for each flow.")
    print()

    for a in analyses:
        print(f"--- {a['flow']} ---")
        print(f"  system prompt:  ~{a['system_prompt_tokens']} tokens (fixed, paid every run)")
        print(f"  user request:   ~{a['request_tokens']} tokens (fixed, paid every run)")
        run_to_show = next((r for r in a["runs"] if r["run"] == args.run), a["runs"][0] if a["runs"] else None)
        if run_to_show:
            print(f"  step-by-step (run {run_to_show['run']}):")
            for s in run_to_show["steps"]:
                print(f"    {s['name']:<28s} in~{s['input_tokens']:>5d}  out~{s['output_tokens']:>5d}")
        print(f"  avg tool-input tokens/run:   ~{a['avg_tool_input_tokens']}")
        print(f"  avg tool-output tokens/run:  ~{a['avg_tool_output_tokens']}")
        print(f"  avg estimated context load:  ~{a['avg_estimated_context_load_tokens']} tokens/run")
        print()

    if len(analyses) >= 2:
        base, other = analyses[0], analyses[1]
        if base["avg_estimated_context_load_tokens"] > 0:
            saving = (1 - other["avg_estimated_context_load_tokens"] / base["avg_estimated_context_load_tokens"]) * 100
            print("=" * 70)
            print(f" Estimated token-load reduction ({other['flow']} vs {base['flow']}): {saving:.1f}%")
            print("=" * 70)

    print()
    print("Caveats (read before citing this number):")
    print("  - This is a heuristic estimate of PAYLOAD size, not a real tokenizer count.")
    print("  - It does not include the model's own reasoning/response tokens —")
    print("    a live agent run would add tool-selection and reasoning tokens on top.")
    print("  - It does not include prompt-cache discounts a real provider might apply")
    print("    to the repeated system prompt across turns.")
    print("  - For an exact, provider-reported number, run the harness in `live` mode.")

    if args.json:
        report["EstimatedTokenAnalysis"] = {
            "method": "max(chars/4, words*1.3) applied to captured tool Input/Output payloads",
            "caveats": [
                "Heuristic estimate of payload size, not a real tokenizer count.",
                "Does not include model reasoning/response tokens (live mode only).",
                "Does not include prompt-cache discounts a real provider might apply.",
            ],
            "flows": analyses,
        }
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
        print(f"\nWrote augmented report to {args.json}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
