#!/usr/bin/env python3
"""
estimate_cost_savings.py — offline $ savings estimator built on estimate_tokens.py.

What this is
------------
Takes the heuristic per-run token estimate from estimate_tokens.py (chars/4 vs
words*1.3 on tool payloads — see that file's docstring for the method and its
caveats) and converts it into a $/call, $/day, $/month, $/year estimate at a
chosen call volume and model price.

This is a SECOND-ORDER estimate: it inherits every caveat of estimate_tokens.py
(directional payload-size heuristic, not a real tokenizer count, no model
reasoning/response tokens included) and adds one more of its own — it assumes
tool-output payloads are billed as INPUT tokens on the agent's next turn, and
tool-input payloads (the arguments the model itself generated to call a tool)
are billed as OUTPUT tokens. That is how a real tool-calling loop is billed,
but the split is inferred, not measured. Treat every number this script
prints as a plausible order of magnitude, not a quote.

Usage
-----
    python3 estimate_cost_savings.py agent-benchmark.json
    python3 estimate_cost_savings.py agent-benchmark.json --calls-per-day 100000
    python3 estimate_cost_savings.py agent-benchmark.json --input-price 3 --output-price 15
"""
import argparse
import json
import sys

# Reference rates, USD per million tokens, current as of Aug 2026. Pass
# --input-price/--output-price for any other model or to stay current as
# prices change; these defaults are a starting point, not a live lookup.
REFERENCE_MODELS = {
    "haiku-4.5": (1, 5),
    "sonnet-5-intro": (2, 10),   # through Aug 31, 2026
    "sonnet-5-standard": (3, 15),  # from Sep 1, 2026
    "opus-5": (5, 25),
}


def estimate_tokens(text) -> int:
    if not text:
        return 0
    chars = len(text)
    words = len(text.split())
    return round(max(chars / 4, words * 1.3))


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


def _flow_runs(report: dict, flow_title: str):
    """Support both report shapes seen in this repo: a nested
    {"Flows":[{"title","runs":[...]}]} shape, and the flat
    {"Results":[{"Flow": "...", "Trace": [...]}]} shape produced directly by
    the .NET harness. Either way, returns the list of runs for one flow."""
    if report.get("Flows"):
        for flow in report["Flows"]:
            title = flow.get("title") or flow.get("id")
            runs = flow.get("runs") or []
            if runs and (runs[0].get("Flow") == flow_title or title == flow_title):
                return runs
    if report.get("Results"):
        return [r for r in report["Results"] if r.get("Flow") == flow_title]
    return []


def flow_io_split(runs: list, flow_title: str) -> dict:
    """Return avg {input_tokens, output_tokens} per run for a flow, split by
    the input/output billing convention described in the module docstring."""
    overhead = estimate_tokens(SYSTEM_PROMPTS.get(flow_title, "")) + estimate_tokens(SCENARIO_REQUEST)
    tool_in_totals, tool_out_totals = [], []
    for run in runs:
        trace = run.get("Trace") or []
        ti = sum(estimate_tokens(ev.get("Input")) for ev in trace)
        to = sum(estimate_tokens(ev.get("Output")) for ev in trace)
        tool_in_totals.append(ti)
        tool_out_totals.append(to)
    n = len(runs) or 1
    avg_tool_in = sum(tool_in_totals) / n   # model-generated tool call args -> OUTPUT tokens
    avg_tool_out = sum(tool_out_totals) / n  # tool results fed back next turn -> INPUT tokens
    return {
        "input_tokens": overhead + avg_tool_out,
        "output_tokens": avg_tool_in,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("report", help="Path to agent-benchmark.json")
    parser.add_argument("--calls-per-day", type=float, default=100_000, help="Agent calls/day to project (default: 100,000)")
    parser.add_argument("--input-price", type=float, default=None, help="USD per million input tokens (overrides --model)")
    parser.add_argument("--output-price", type=float, default=None, help="USD per million output tokens (overrides --model)")
    parser.add_argument("--model", choices=list(REFERENCE_MODELS), default=None, help="Use a reference model's price instead of custom prices")
    args = parser.parse_args()

    with open(args.report, "r", encoding="utf-8") as f:
        report = json.load(f)

    conv_runs = _flow_runs(report, "Conventional application/AI flow")
    found_runs = _flow_runs(report, "Foundgine semantic flow")
    if not conv_runs or not found_runs:
        print("Report needs per-run Trace data for both flows (either a 'Flows' array or a flat 'Results' array with a 'Flow' field).", file=sys.stderr)
        return 1

    conv = flow_io_split(conv_runs, "Conventional application/AI flow")
    found = flow_io_split(found_runs, "Foundgine semantic flow")

    price_sets = []
    if args.input_price is not None or args.output_price is not None:
        pin = args.input_price if args.input_price is not None else 3
        pout = args.output_price if args.output_price is not None else 15
        price_sets.append(("custom", pin, pout))
    elif args.model:
        pin, pout = REFERENCE_MODELS[args.model]
        price_sets.append((args.model, pin, pout))
    else:
        price_sets = [(name, pin, pout) for name, (pin, pout) in REFERENCE_MODELS.items()]

    print("=" * 78)
    print(" Estimated $ savings from token-load reduction (heuristic, see caveats)")
    print("=" * 78)
    print(f"Conventional: ~{conv['input_tokens']:.0f} input + ~{conv['output_tokens']:.0f} output tokens/call")
    print(f"Foundgine:    ~{found['input_tokens']:.0f} input + ~{found['output_tokens']:.0f} output tokens/call")
    print(f"Projected at {args.calls_per_day:,.0f} agent calls/day\n")

    for name, pin, pout in price_sets:
        conv_cost = conv["input_tokens"] / 1e6 * pin + conv["output_tokens"] / 1e6 * pout
        found_cost = found["input_tokens"] / 1e6 * pin + found["output_tokens"] / 1e6 * pout
        saved = conv_cost - found_cost
        pct = (saved / conv_cost * 100) if conv_cost else 0
        day = saved * args.calls_per_day
        month = day * 30
        year = day * 365
        print(f"--- {name} (${pin}/${pout} per MTok) ---")
        print(f"  per call: conventional ${conv_cost:.6f} | foundgine ${found_cost:.6f} | saved ${saved:.6f} ({pct:.1f}%)")
        print(f"  saves ${day:,.2f}/day | ${month:,.0f}/month | ${year:,.0f}/year\n")

    print("Caveats:")
    print("  - Inherits every caveat of estimate_tokens.py: heuristic payload-size")
    print("    estimate, not a real tokenizer count, and excludes model reasoning/")
    print("    response tokens entirely (a live run would add those to BOTH flows,")
    print("    which would change the absolute totals but not necessarily the ratio).")
    print("  - The input/output split (tool-output -> input, tool-input -> output) is")
    print("    inferred from how a tool-calling loop is normally billed, not measured.")
    print("  - Reference prices are current as of Aug 2026 and will drift; pass")
    print("    --input-price/--output-price for your actual model and rate.")
    print("  - Treat every $ figure here as an order of magnitude for planning, not")
    print("    a committed savings number. Confirm with a `live`-mode run before")
    print("    quoting this externally.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
