# Foundgine Agent End-to-End Benchmark — customer-exposure-review

Generated: `2026-09-01T12:10:23.5712049+00:00`  
Mode: `replay`  
Runs: `10` measured / `2` warmups

## Headline

- Input/total-token saving (provider-reported): **N/A** — `replay` mode makes no model calls, so provider token usage is
  always zero. Run in `live` mode for real numbers.
- Estimated context-load saving (heuristic, all modes): **67.2%** — see "Tokens vs. API time vs. application load"
  below.
- Tool-call saving: **42.9%**
- Model-call saving: **0.0%**
- Wall-clock change: **44.1%**
- Same final state: **True**

## Measured averages

| Metric                                                   | Conventional | Foundgine |
|----------------------------------------------------------|-------------:|----------:|
| Wall clock (ms)                                          |         64.7 |      93.2 |
| Model time (ms)                                          |          0.0 |       0.0 |
| Tool time (ms)                                           |       2708.9 |    4556.7 |
| Model calls                                              |          0.0 |       0.0 |
| Tool calls                                               |        448.0 |     256.0 |
| Input tokens                                             |          0.0 |       0.0 |
| Output tokens                                            |          0.0 |       0.0 |
| Total tokens                                             |          0.0 |       0.0 |
| Cached input tokens                                      |          0.0 |       0.0 |
| Estimated tool-input tokens (heuristic)                  |       2359.0 |    1216.0 |
| Estimated tool-output tokens (heuristic)                 |      47578.0 |    7223.0 |
| Estimated context load (heuristic, incl. system+request) |      61713.0 |   20215.0 |

## Method

Both flows run against the same PostgreSQL fixture, the same authenticated benchmark request, the same deterministic
Customer 1 graph, and the same final-state assertion. Live mode records provider-reported usage; replay mode is for
validating the harness and must not be presented as real model-token evidence.

## Tokens vs. API time vs. application load

These are three different measurements and none of them substitutes for the others:

- **Tokens** measure how much *context* an agent has to carry — the size of what it reads and writes. This is what
  drives per-request API cost and how much of a model's context window a task consumes. `Input/Output/TotalTokens` above
  are real, provider-reported numbers and are only populated in `live` mode. The `Estimated *` rows are an offline
  chars/words heuristic (see `TokenEstimator` in this file) that approximates the same thing from tool payload sizes
  alone, so replay mode still gives a directional signal instead of a hard zero.
- **API/model time** (`ModelTimeMs`) measures how long the model spent thinking and responding — wall-clock time
  actually billed to inference. It moves with tokens but not proportionally: a short, hard reasoning turn can cost more
  time than a long, easy one.
- **Application load** (`ToolTimeMs`, `WallClockMs`, CPU, memory) measures how long and how much compute the
  *application side* — the tool calls, the database, the semantic engine — spent doing the work the agent asked for.
  This can go up even when tokens go down: Foundgine's `WallClockMs` was higher than the conventional flow's in this
  replay precisely because it front-loads more resolution work into the application boundary so the agent doesn't have
  to.

A flow can therefore win on one axis and lose on another. The headline claim this benchmark supports is narrower than "
faster" or "cheaper": in this scenario, the semantic boundary reduced the number and size of round trips the agent had
to coordinate (tool calls, and — per the estimate above — token load), at the cost of higher measured application
wall-clock time in replay mode. Judge the trade-off against what you are optimizing for: per-call API spend and
context-window pressure favor fewer/smaller round trips; raw end-to-end latency does not automatically follow.

## Scenario

Find the highest-exposure eligible customer for the authenticated tenant with exposure above the configured threshold,
then perform the authorized review mutation and verify the final state. The benchmark compares the conventional
discovery/tool choreography against the Foundgine semantic capability choreography while asserting the same final state.
