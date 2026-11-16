# Why Foundgine: what the evidence supports today

Positioning: Foundgine derives its semantic model, metadata and execution maps **by convention** and generates them at
compile time (Roslyn source generator, Native AOT-safe) on top of real frameworks (EF Core, ASP.NET, Temporal,
Microsoft Agent Framework), so one application-controlled boundary serves code, GraphQL, MCP and agents instead of
every surface re-implementing validation, authorization and query logic.

This page is a **claims register**: each claim is tied to the measurement that would prove it and its current status.
Status values: **Supported** (recorded data, interval, equivalence passed), **Partly** (data exists with known gaps),
**Not yet measured**, **Contradicted today**. Update the status only from a claim-grade `report.md`.

| # | Claim | Evidence required (tool) | Status | Wording that is safe now |
|---|---|---|---|---|
| 1 | Higher read throughput than Hot Chocolate + EF Core on the CoffeeBeanery query | >=5 sessions, equivalence, CPU/RSS (`fpbench run`) | Partly: published runs show 7-36x over 2 sessions on one host (re-analysed in the Performance Report); no equivalence check, no telemetry | "7-36x in two sessions on one host; not yet independently replicated" |
| 2 | Batched writes 1.5-5.7x faster (batch 10/50); single writes at parity | same | Partly (same gaps) | state parity at batch 1 alongside the batch result |
| 3 | Uses fewer CPU/memory resources | resource telemetry per cell | Not yet measured (existing reports hold zeros; root cause fixed in `sampler.py`) | omit |
| 4 | Authorization enforced at the boundary without a speed penalty | real policy on the Foundgine arm (fairness control 6.1) | Not yet measured (benchmark uses allow-all) | omit |
| 5 | Less hand-written scaffolding (convention + generation) | `footprint.py`, change-cost and time-to-first-endpoint on arm C | **Contradicted today**: benchmark Foundgine API is 379 code lines vs 202 for Hot Chocolate, with 13 manual registrations and no generator attributes | omit until arm C exists |
| 6 | Works under Native AOT | `src/csharp/aot/validate-aot.sh`, CI matrix, warning baseline | Partly: gate exists and is fail-closed; the checked-in matrix does not claim CI has run | cite only CI artifacts for a named RID |
| 7 | Faster startup / smaller footprint with AOT | `convention/startup_meter.py`, JIT vs AOT of arm C | Not yet measured | omit |
| 8 | Enterprise integration (CRM + ERP + Temporal) performs under business load | journey runs (`fpbench journeys`) | Not yet measured: the e2e example was never built or run | omit |
| 9 | Fewer agent tool calls | published Runs 1-3 (counts) | Supported as counts; latency impact mixed (Run 1 91% slower) | quote with the latency caveat |
| 10 | Lower agent context cost | provider-reported token usage | Estimate only (chars/4, replay mode) | "estimated" |

## What to show in the story, in order
1. **Fewer places to be wrong**: one boundary, N surfaces. Evidence: security/conformance tests and tool-call counts, not speed.
2. **Convention, generated not hand-written**: entity attributes in, maps/metadata out. Evidence: arm C effort metrics (pending).
3. **Real frameworks underneath**: same EF entities as the baseline, so no duplicate model. Evidence: arm C shares entity classes with arm A.
4. **Fast enough, and measured honestly**: throughput with intervals, equivalence, resource cost, open-loop tails.
5. **Operable**: outbox lag, drain time after an outage, sustainable journey rate.

## Rules for any number published from this suite
- Report must say **Claim grade: YES** (>=5 independent sessions, equivalent responses, telemetry, disclosed environment, open-loop run).
- Quote the ratio with its interval; link the `results/` folder it came from.
- Engine speed (arm B vs A), convention effort (arm C vs B) and request shape (batch tool controls) are separate claims.
- Excluded outlier cells (Run 4 at 10,000 customers/8 concurrent, Run 5b at 100 customers/64 concurrent, Hot Chocolate query at 16 concurrent) stay out of headlines until explained.
