# Foundgine Agent End-to-End Benchmark — customer-exposure-review-query-mutation-query-mutation

Generated: `2026-08-30T03:54:19.4015959+00:00`  
Mode: `replay`  
Concurrency: `1`; runs: `1` measured / `1` warmups; fixture customer `1`

## Headline

- Estimated context-load saving: **69.0%**
- Agent/tool round-trip saving: **88.9%**
- Agent/tool payload saving: **95.0%**
- Tool-call saving: **88.9%**
- Model-call saving: **0.0%**
- Provider-reported input-token saving: **Not measured (replay mode)**
- Provider-reported total-token saving: **Not measured (replay mode)**
- Expected final state verified: **False**
- Verification failures: **1**

## Method

Estimated tokens use the current benchmark method: `max(chars / 4, words × 1.3)`, rounded to the nearest whole token. The estimator is applied to every recorded tool input and tool output, then the fixed system prompt and scenario request are added once per run. This is a directional BPE approximation, not a provider tokenizer. It does not include model reasoning or model response tokens. Live runs also record provider-reported usage, which is the authoritative token measurement.

## Averages

| Metric                        | Conventional | Foundgine |
| ----------------------------- | -----------: | --------: |
| Wall clock (ms)               |         13.3 |       3.7 |
| Model time (ms)               |          0.0 |       0.0 |
| Tool time (ms)                |         11.8 |       0.0 |
| Success rate (%)              |        100.0 |       0.0 |
| p50 wall (ms)                 |         13.3 |       0.0 |
| p95 wall (ms)                 |         13.3 |       0.0 |
| p99 wall (ms)                 |         13.3 |       0.0 |
| Peak active HTTP requests     |          0.0 |       1.0 |
| HTTP retries                  |          0.0 |       0.0 |
| Model calls                   |          0.0 |       0.0 |
| Tool calls                    |          9.0 |       1.0 |
| Agent/tool round trips        |          9.0 |       1.0 |
| Agent/tool payload bytes      |       3372.0 |     169.0 |
| Estimated tool-input tokens   |         61.0 |       1.0 |
| Estimated tool-output tokens  |        783.0 |      42.0 |
| Estimated context-load tokens |       1150.0 |     356.0 |
| Provider input tokens         |          0.0 |       0.0 |
| Provider output tokens        |          0.0 |       0.0 |
| Provider total tokens         |          0.0 |       0.0 |
| Cached input tokens           |          0.0 |       0.0 |

## Expected-state verification

Each flow is compared against an explicit expected state generated from the reset baseline. The process is intentionally stateful: QUERY #1 reads the exposure graph, MUTATION #1 marks the customer reviewed, QUERY #2 verifies that intermediate state, MUTATION #2 completes the remediation follow-up, and QUERY #3 verifies the final state. The expected state preserves CustomerKey and all relationship/contract/transaction counts and exposure, with deterministic FullName transitions.

| Run | Flow         | Customer | Match | Differences                   |
| --: | ------------ | -------: | ----- | ----------------------------- |
|   1 | Conventional |        1 | PASS  | —                             |
|   1 | Foundgine    |        1 | FAIL  | Final state was not recorded. |

## Trace interpretation

The estimated context-load metric is intentionally narrower than a model's true prompt-token count. It measures the recorded application tool payloads plus the fixed system/request overhead specified by the benchmark methodology. It is useful for comparing how much tool-result context each architecture forces through the agent loop; it is not a replacement for provider-reported usage.
