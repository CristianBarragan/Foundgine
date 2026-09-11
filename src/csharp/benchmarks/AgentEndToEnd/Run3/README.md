# Agent End-to-End Benchmark — Run 3: Total Cost & Efficiency

Run 3 repeats the Run 1 customer-exposure scenario with a longer measurement window and captures Docker resource telemetry alongside the agent trace.

## What it measures

- conventional application/AI flow vs Foundgine semantic flow
- model/tool calls and estimated token/context load
- wall-clock, model time and tool time
- Docker CPU percentage
- Docker memory usage and peak memory
- Docker network RX/TX
- Docker block I/O read/write
- Docker process count (PIDs)
- per-run JSON/Markdown benchmark reports
- raw Docker telemetry CSV plus summary JSON

## Recommended run

```powershell
.\run-agent-benchmark.ps1 -Mode replay -Warmups 5 -Runs 20
```

For real model billing, use live mode and provide `AGENT_MODEL_ENDPOINT`, `AGENT_MODEL_API_KEY` and `AGENT_MODEL`.

Run 3 deliberately does not claim measured electricity consumption. Docker stats provide CPU/memory/I/O telemetry; actual Wh requires host/server power telemetry.


To publish the completed report into the shared `docs-site/assets/agent-benchmark/` folder, add `-Publish` to the benchmark command, or run `publish-report.ps1` directly.
