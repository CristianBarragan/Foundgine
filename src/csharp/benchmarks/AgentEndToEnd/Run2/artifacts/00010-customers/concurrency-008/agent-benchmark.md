# Foundgine Agent End-to-End Benchmark — customer-exposure-review-query-mutation-query-mutation

Generated: `2026-09-01T11:25:57.5190101+00:00`  
Mode: `replay`  
Concurrency: `8`; runs: `30` measured / `5` warmups; fixture customer `1`

## Headline

- Estimated context-load saving: **56.9%**
- Agent/tool round-trip saving: **33.3%**
- Agent/tool payload saving: **78.2%**
- Tool-call saving: **33.3%**
- Model-call saving: **0.0%**
- Provider-reported input-token saving: **Not measured (replay mode)**
- Provider-reported total-token saving: **Not measured (replay mode)**
- Expected final state verified: **True**
- Verification failures: **0**

## Method

Estimated tokens use the current benchmark method: `max(chars / 4, words × 1.3)`, rounded to the nearest whole token.
The estimator is applied to every recorded tool input and tool output, then the fixed system prompt and scenario request
are added once per run. This is a directional BPE approximation, not a provider tokenizer. It does not include model
reasoning or model response tokens. Live runs also record provider-reported usage, which is the authoritative token
measurement.

## Averages

| Metric                        | Conventional | Foundgine |
|-------------------------------|-------------:|----------:|
| Wall clock (ms)               |         24.3 |      26.0 |
| Model time (ms)               |          0.0 |       0.0 |
| Tool time (ms)                |         21.7 |      23.0 |
| Success rate (%)              |        100.0 |     100.0 |
| p50 wall (ms)                 |         24.3 |      25.8 |
| p95 wall (ms)                 |         28.0 |      32.2 |
| p99 wall (ms)                 |         30.5 |      34.3 |
| Peak active HTTP requests     |          0.0 |       8.0 |
| HTTP retries                  |          0.0 |       0.0 |
| Model calls                   |          0.0 |       0.0 |
| Tool calls                    |          9.0 |       6.0 |
| Agent/tool round trips        |          9.0 |       6.0 |
| Agent/tool payload bytes      |       3428.9 |     747.0 |
| Estimated tool-input tokens   |         61.0 |      33.0 |
| Estimated tool-output tokens  |        797.0 |     156.0 |
| Estimated context-load tokens |       1164.0 |     502.0 |
| Provider input tokens         |          0.0 |       0.0 |
| Provider output tokens        |          0.0 |       0.0 |
| Provider total tokens         |          0.0 |       0.0 |
| Cached input tokens           |          0.0 |       0.0 |

## Expected-state verification

Each flow is compared against an explicit expected state generated from the reset baseline. The process is intentionally
stateful: QUERY #1 reads the exposure graph, MUTATION #1 marks the customer reviewed, QUERY #2 verifies that
intermediate state, MUTATION #2 completes the remediation follow-up, and QUERY #3 verifies the final state. The expected
state preserves CustomerKey and all relationship/contract/transaction counts and exposure, with deterministic FullName
transitions.

| Run | Flow         | Customer | Match | Differences |
|----:|--------------|---------:|-------|-------------|
|   1 | Conventional |        1 | PASS  | —           |
|   1 | Conventional |        2 | PASS  | —           |
|   1 | Conventional |        3 | PASS  | —           |
|   1 | Conventional |        4 | PASS  | —           |
|   1 | Conventional |        5 | PASS  | —           |
|   1 | Conventional |        6 | PASS  | —           |
|   1 | Conventional |        7 | PASS  | —           |
|   1 | Conventional |        8 | PASS  | —           |
|   2 | Conventional |        1 | PASS  | —           |
|   2 | Conventional |        2 | PASS  | —           |
|   2 | Conventional |        3 | PASS  | —           |
|   2 | Conventional |        4 | PASS  | —           |
|   2 | Conventional |        5 | PASS  | —           |
|   2 | Conventional |        6 | PASS  | —           |
|   2 | Conventional |        7 | PASS  | —           |
|   2 | Conventional |        8 | PASS  | —           |
|   3 | Conventional |        1 | PASS  | —           |
|   3 | Conventional |        2 | PASS  | —           |
|   3 | Conventional |        3 | PASS  | —           |
|   3 | Conventional |        4 | PASS  | —           |
|   3 | Conventional |        5 | PASS  | —           |
|   3 | Conventional |        6 | PASS  | —           |
|   3 | Conventional |        7 | PASS  | —           |
|   3 | Conventional |        8 | PASS  | —           |
|   4 | Conventional |        1 | PASS  | —           |
|   4 | Conventional |        2 | PASS  | —           |
|   4 | Conventional |        3 | PASS  | —           |
|   4 | Conventional |        4 | PASS  | —           |
|   4 | Conventional |        5 | PASS  | —           |
|   4 | Conventional |        6 | PASS  | —           |
|   4 | Conventional |        7 | PASS  | —           |
|   4 | Conventional |        8 | PASS  | —           |
|   5 | Conventional |        1 | PASS  | —           |
|   5 | Conventional |        2 | PASS  | —           |
|   5 | Conventional |        3 | PASS  | —           |
|   5 | Conventional |        4 | PASS  | —           |
|   5 | Conventional |        5 | PASS  | —           |
|   5 | Conventional |        6 | PASS  | —           |
|   5 | Conventional |        7 | PASS  | —           |
|   5 | Conventional |        8 | PASS  | —           |
|   6 | Conventional |        1 | PASS  | —           |
|   6 | Conventional |        2 | PASS  | —           |
|   6 | Conventional |        3 | PASS  | —           |
|   6 | Conventional |        4 | PASS  | —           |
|   6 | Conventional |        5 | PASS  | —           |
|   6 | Conventional |        6 | PASS  | —           |
|   6 | Conventional |        7 | PASS  | —           |
|   6 | Conventional |        8 | PASS  | —           |
|   7 | Conventional |        1 | PASS  | —           |
|   7 | Conventional |        2 | PASS  | —           |
|   7 | Conventional |        3 | PASS  | —           |
|   7 | Conventional |        4 | PASS  | —           |
|   7 | Conventional |        5 | PASS  | —           |
|   7 | Conventional |        6 | PASS  | —           |
|   7 | Conventional |        7 | PASS  | —           |
|   7 | Conventional |        8 | PASS  | —           |
|   8 | Conventional |        1 | PASS  | —           |
|   8 | Conventional |        2 | PASS  | —           |
|   8 | Conventional |        3 | PASS  | —           |
|   8 | Conventional |        4 | PASS  | —           |
|   8 | Conventional |        5 | PASS  | —           |
|   8 | Conventional |        6 | PASS  | —           |
|   8 | Conventional |        7 | PASS  | —           |
|   8 | Conventional |        8 | PASS  | —           |
|   9 | Conventional |        1 | PASS  | —           |
|   9 | Conventional |        2 | PASS  | —           |
|   9 | Conventional |        3 | PASS  | —           |
|   9 | Conventional |        4 | PASS  | —           |
|   9 | Conventional |        5 | PASS  | —           |
|   9 | Conventional |        6 | PASS  | —           |
|   9 | Conventional |        7 | PASS  | —           |
|   9 | Conventional |        8 | PASS  | —           |
|  10 | Conventional |        1 | PASS  | —           |
|  10 | Conventional |        2 | PASS  | —           |
|  10 | Conventional |        3 | PASS  | —           |
|  10 | Conventional |        4 | PASS  | —           |
|  10 | Conventional |        5 | PASS  | —           |
|  10 | Conventional |        6 | PASS  | —           |
|  10 | Conventional |        7 | PASS  | —           |
|  10 | Conventional |        8 | PASS  | —           |
|  11 | Conventional |        1 | PASS  | —           |
|  11 | Conventional |        2 | PASS  | —           |
|  11 | Conventional |        3 | PASS  | —           |
|  11 | Conventional |        4 | PASS  | —           |
|  11 | Conventional |        5 | PASS  | —           |
|  11 | Conventional |        6 | PASS  | —           |
|  11 | Conventional |        7 | PASS  | —           |
|  11 | Conventional |        8 | PASS  | —           |
|  12 | Conventional |        1 | PASS  | —           |
|  12 | Conventional |        2 | PASS  | —           |
|  12 | Conventional |        3 | PASS  | —           |
|  12 | Conventional |        4 | PASS  | —           |
|  12 | Conventional |        5 | PASS  | —           |
|  12 | Conventional |        6 | PASS  | —           |
|  12 | Conventional |        7 | PASS  | —           |
|  12 | Conventional |        8 | PASS  | —           |
|  13 | Conventional |        1 | PASS  | —           |
|  13 | Conventional |        2 | PASS  | —           |
|  13 | Conventional |        3 | PASS  | —           |
|  13 | Conventional |        4 | PASS  | —           |
|  13 | Conventional |        5 | PASS  | —           |
|  13 | Conventional |        6 | PASS  | —           |
|  13 | Conventional |        7 | PASS  | —           |
|  13 | Conventional |        8 | PASS  | —           |
|  14 | Conventional |        1 | PASS  | —           |
|  14 | Conventional |        2 | PASS  | —           |
|  14 | Conventional |        3 | PASS  | —           |
|  14 | Conventional |        4 | PASS  | —           |
|  14 | Conventional |        5 | PASS  | —           |
|  14 | Conventional |        6 | PASS  | —           |
|  14 | Conventional |        7 | PASS  | —           |
|  14 | Conventional |        8 | PASS  | —           |
|  15 | Conventional |        1 | PASS  | —           |
|  15 | Conventional |        2 | PASS  | —           |
|  15 | Conventional |        3 | PASS  | —           |
|  15 | Conventional |        4 | PASS  | —           |
|  15 | Conventional |        5 | PASS  | —           |
|  15 | Conventional |        6 | PASS  | —           |
|  15 | Conventional |        7 | PASS  | —           |
|  15 | Conventional |        8 | PASS  | —           |
|  16 | Conventional |        1 | PASS  | —           |
|  16 | Conventional |        2 | PASS  | —           |
|  16 | Conventional |        3 | PASS  | —           |
|  16 | Conventional |        4 | PASS  | —           |
|  16 | Conventional |        5 | PASS  | —           |
|  16 | Conventional |        6 | PASS  | —           |
|  16 | Conventional |        7 | PASS  | —           |
|  16 | Conventional |        8 | PASS  | —           |
|  17 | Conventional |        1 | PASS  | —           |
|  17 | Conventional |        2 | PASS  | —           |
|  17 | Conventional |        3 | PASS  | —           |
|  17 | Conventional |        4 | PASS  | —           |
|  17 | Conventional |        5 | PASS  | —           |
|  17 | Conventional |        6 | PASS  | —           |
|  17 | Conventional |        7 | PASS  | —           |
|  17 | Conventional |        8 | PASS  | —           |
|  18 | Conventional |        1 | PASS  | —           |
|  18 | Conventional |        2 | PASS  | —           |
|  18 | Conventional |        3 | PASS  | —           |
|  18 | Conventional |        4 | PASS  | —           |
|  18 | Conventional |        5 | PASS  | —           |
|  18 | Conventional |        6 | PASS  | —           |
|  18 | Conventional |        7 | PASS  | —           |
|  18 | Conventional |        8 | PASS  | —           |
|  19 | Conventional |        1 | PASS  | —           |
|  19 | Conventional |        2 | PASS  | —           |
|  19 | Conventional |        3 | PASS  | —           |
|  19 | Conventional |        4 | PASS  | —           |
|  19 | Conventional |        5 | PASS  | —           |
|  19 | Conventional |        6 | PASS  | —           |
|  19 | Conventional |        7 | PASS  | —           |
|  19 | Conventional |        8 | PASS  | —           |
|  20 | Conventional |        1 | PASS  | —           |
|  20 | Conventional |        2 | PASS  | —           |
|  20 | Conventional |        3 | PASS  | —           |
|  20 | Conventional |        4 | PASS  | —           |
|  20 | Conventional |        5 | PASS  | —           |
|  20 | Conventional |        6 | PASS  | —           |
|  20 | Conventional |        7 | PASS  | —           |
|  20 | Conventional |        8 | PASS  | —           |
|  21 | Conventional |        1 | PASS  | —           |
|  21 | Conventional |        2 | PASS  | —           |
|  21 | Conventional |        3 | PASS  | —           |
|  21 | Conventional |        4 | PASS  | —           |
|  21 | Conventional |        5 | PASS  | —           |
|  21 | Conventional |        6 | PASS  | —           |
|  21 | Conventional |        7 | PASS  | —           |
|  21 | Conventional |        8 | PASS  | —           |
|  22 | Conventional |        1 | PASS  | —           |
|  22 | Conventional |        2 | PASS  | —           |
|  22 | Conventional |        3 | PASS  | —           |
|  22 | Conventional |        4 | PASS  | —           |
|  22 | Conventional |        5 | PASS  | —           |
|  22 | Conventional |        6 | PASS  | —           |
|  22 | Conventional |        7 | PASS  | —           |
|  22 | Conventional |        8 | PASS  | —           |
|  23 | Conventional |        1 | PASS  | —           |
|  23 | Conventional |        2 | PASS  | —           |
|  23 | Conventional |        3 | PASS  | —           |
|  23 | Conventional |        4 | PASS  | —           |
|  23 | Conventional |        5 | PASS  | —           |
|  23 | Conventional |        6 | PASS  | —           |
|  23 | Conventional |        7 | PASS  | —           |
|  23 | Conventional |        8 | PASS  | —           |
|  24 | Conventional |        1 | PASS  | —           |
|  24 | Conventional |        2 | PASS  | —           |
|  24 | Conventional |        3 | PASS  | —           |
|  24 | Conventional |        4 | PASS  | —           |
|  24 | Conventional |        5 | PASS  | —           |
|  24 | Conventional |        6 | PASS  | —           |
|  24 | Conventional |        7 | PASS  | —           |
|  24 | Conventional |        8 | PASS  | —           |
|  25 | Conventional |        1 | PASS  | —           |
|  25 | Conventional |        2 | PASS  | —           |
|  25 | Conventional |        3 | PASS  | —           |
|  25 | Conventional |        4 | PASS  | —           |
|  25 | Conventional |        5 | PASS  | —           |
|  25 | Conventional |        6 | PASS  | —           |
|  25 | Conventional |        7 | PASS  | —           |
|  25 | Conventional |        8 | PASS  | —           |
|  26 | Conventional |        1 | PASS  | —           |
|  26 | Conventional |        2 | PASS  | —           |
|  26 | Conventional |        3 | PASS  | —           |
|  26 | Conventional |        4 | PASS  | —           |
|  26 | Conventional |        5 | PASS  | —           |
|  26 | Conventional |        6 | PASS  | —           |
|  26 | Conventional |        7 | PASS  | —           |
|  26 | Conventional |        8 | PASS  | —           |
|  27 | Conventional |        1 | PASS  | —           |
|  27 | Conventional |        2 | PASS  | —           |
|  27 | Conventional |        3 | PASS  | —           |
|  27 | Conventional |        4 | PASS  | —           |
|  27 | Conventional |        5 | PASS  | —           |
|  27 | Conventional |        6 | PASS  | —           |
|  27 | Conventional |        7 | PASS  | —           |
|  27 | Conventional |        8 | PASS  | —           |
|  28 | Conventional |        1 | PASS  | —           |
|  28 | Conventional |        2 | PASS  | —           |
|  28 | Conventional |        3 | PASS  | —           |
|  28 | Conventional |        4 | PASS  | —           |
|  28 | Conventional |        5 | PASS  | —           |
|  28 | Conventional |        6 | PASS  | —           |
|  28 | Conventional |        7 | PASS  | —           |
|  28 | Conventional |        8 | PASS  | —           |
|  29 | Conventional |        1 | PASS  | —           |
|  29 | Conventional |        2 | PASS  | —           |
|  29 | Conventional |        3 | PASS  | —           |
|  29 | Conventional |        4 | PASS  | —           |
|  29 | Conventional |        5 | PASS  | —           |
|  29 | Conventional |        6 | PASS  | —           |
|  29 | Conventional |        7 | PASS  | —           |
|  29 | Conventional |        8 | PASS  | —           |
|  30 | Conventional |        1 | PASS  | —           |
|  30 | Conventional |        2 | PASS  | —           |
|  30 | Conventional |        3 | PASS  | —           |
|  30 | Conventional |        4 | PASS  | —           |
|  30 | Conventional |        5 | PASS  | —           |
|  30 | Conventional |        6 | PASS  | —           |
|  30 | Conventional |        7 | PASS  | —           |
|  30 | Conventional |        8 | PASS  | —           |
|   1 | Foundgine    |        1 | PASS  | —           |
|   1 | Foundgine    |        2 | PASS  | —           |
|   1 | Foundgine    |        3 | PASS  | —           |
|   1 | Foundgine    |        4 | PASS  | —           |
|   1 | Foundgine    |        5 | PASS  | —           |
|   1 | Foundgine    |        6 | PASS  | —           |
|   1 | Foundgine    |        7 | PASS  | —           |
|   1 | Foundgine    |        8 | PASS  | —           |
|   2 | Foundgine    |        1 | PASS  | —           |
|   2 | Foundgine    |        2 | PASS  | —           |
|   2 | Foundgine    |        3 | PASS  | —           |
|   2 | Foundgine    |        4 | PASS  | —           |
|   2 | Foundgine    |        5 | PASS  | —           |
|   2 | Foundgine    |        6 | PASS  | —           |
|   2 | Foundgine    |        7 | PASS  | —           |
|   2 | Foundgine    |        8 | PASS  | —           |
|   3 | Foundgine    |        1 | PASS  | —           |
|   3 | Foundgine    |        2 | PASS  | —           |
|   3 | Foundgine    |        3 | PASS  | —           |
|   3 | Foundgine    |        4 | PASS  | —           |
|   3 | Foundgine    |        5 | PASS  | —           |
|   3 | Foundgine    |        6 | PASS  | —           |
|   3 | Foundgine    |        7 | PASS  | —           |
|   3 | Foundgine    |        8 | PASS  | —           |
|   4 | Foundgine    |        1 | PASS  | —           |
|   4 | Foundgine    |        2 | PASS  | —           |
|   4 | Foundgine    |        3 | PASS  | —           |
|   4 | Foundgine    |        4 | PASS  | —           |
|   4 | Foundgine    |        5 | PASS  | —           |
|   4 | Foundgine    |        6 | PASS  | —           |
|   4 | Foundgine    |        7 | PASS  | —           |
|   4 | Foundgine    |        8 | PASS  | —           |
|   5 | Foundgine    |        1 | PASS  | —           |
|   5 | Foundgine    |        2 | PASS  | —           |
|   5 | Foundgine    |        3 | PASS  | —           |
|   5 | Foundgine    |        4 | PASS  | —           |
|   5 | Foundgine    |        5 | PASS  | —           |
|   5 | Foundgine    |        6 | PASS  | —           |
|   5 | Foundgine    |        7 | PASS  | —           |
|   5 | Foundgine    |        8 | PASS  | —           |
|   6 | Foundgine    |        1 | PASS  | —           |
|   6 | Foundgine    |        2 | PASS  | —           |
|   6 | Foundgine    |        3 | PASS  | —           |
|   6 | Foundgine    |        4 | PASS  | —           |
|   6 | Foundgine    |        5 | PASS  | —           |
|   6 | Foundgine    |        6 | PASS  | —           |
|   6 | Foundgine    |        7 | PASS  | —           |
|   6 | Foundgine    |        8 | PASS  | —           |
|   7 | Foundgine    |        1 | PASS  | —           |
|   7 | Foundgine    |        2 | PASS  | —           |
|   7 | Foundgine    |        3 | PASS  | —           |
|   7 | Foundgine    |        4 | PASS  | —           |
|   7 | Foundgine    |        5 | PASS  | —           |
|   7 | Foundgine    |        6 | PASS  | —           |
|   7 | Foundgine    |        7 | PASS  | —           |
|   7 | Foundgine    |        8 | PASS  | —           |
|   8 | Foundgine    |        1 | PASS  | —           |
|   8 | Foundgine    |        2 | PASS  | —           |
|   8 | Foundgine    |        3 | PASS  | —           |
|   8 | Foundgine    |        4 | PASS  | —           |
|   8 | Foundgine    |        5 | PASS  | —           |
|   8 | Foundgine    |        6 | PASS  | —           |
|   8 | Foundgine    |        7 | PASS  | —           |
|   8 | Foundgine    |        8 | PASS  | —           |
|   9 | Foundgine    |        1 | PASS  | —           |
|   9 | Foundgine    |        2 | PASS  | —           |
|   9 | Foundgine    |        3 | PASS  | —           |
|   9 | Foundgine    |        4 | PASS  | —           |
|   9 | Foundgine    |        5 | PASS  | —           |
|   9 | Foundgine    |        6 | PASS  | —           |
|   9 | Foundgine    |        7 | PASS  | —           |
|   9 | Foundgine    |        8 | PASS  | —           |
|  10 | Foundgine    |        1 | PASS  | —           |
|  10 | Foundgine    |        2 | PASS  | —           |
|  10 | Foundgine    |        3 | PASS  | —           |
|  10 | Foundgine    |        4 | PASS  | —           |
|  10 | Foundgine    |        5 | PASS  | —           |
|  10 | Foundgine    |        6 | PASS  | —           |
|  10 | Foundgine    |        7 | PASS  | —           |
|  10 | Foundgine    |        8 | PASS  | —           |
|  11 | Foundgine    |        1 | PASS  | —           |
|  11 | Foundgine    |        2 | PASS  | —           |
|  11 | Foundgine    |        3 | PASS  | —           |
|  11 | Foundgine    |        4 | PASS  | —           |
|  11 | Foundgine    |        5 | PASS  | —           |
|  11 | Foundgine    |        6 | PASS  | —           |
|  11 | Foundgine    |        7 | PASS  | —           |
|  11 | Foundgine    |        8 | PASS  | —           |
|  12 | Foundgine    |        1 | PASS  | —           |
|  12 | Foundgine    |        2 | PASS  | —           |
|  12 | Foundgine    |        3 | PASS  | —           |
|  12 | Foundgine    |        4 | PASS  | —           |
|  12 | Foundgine    |        5 | PASS  | —           |
|  12 | Foundgine    |        6 | PASS  | —           |
|  12 | Foundgine    |        7 | PASS  | —           |
|  12 | Foundgine    |        8 | PASS  | —           |
|  13 | Foundgine    |        1 | PASS  | —           |
|  13 | Foundgine    |        2 | PASS  | —           |
|  13 | Foundgine    |        3 | PASS  | —           |
|  13 | Foundgine    |        4 | PASS  | —           |
|  13 | Foundgine    |        5 | PASS  | —           |
|  13 | Foundgine    |        6 | PASS  | —           |
|  13 | Foundgine    |        7 | PASS  | —           |
|  13 | Foundgine    |        8 | PASS  | —           |
|  14 | Foundgine    |        1 | PASS  | —           |
|  14 | Foundgine    |        2 | PASS  | —           |
|  14 | Foundgine    |        3 | PASS  | —           |
|  14 | Foundgine    |        4 | PASS  | —           |
|  14 | Foundgine    |        5 | PASS  | —           |
|  14 | Foundgine    |        6 | PASS  | —           |
|  14 | Foundgine    |        7 | PASS  | —           |
|  14 | Foundgine    |        8 | PASS  | —           |
|  15 | Foundgine    |        1 | PASS  | —           |
|  15 | Foundgine    |        2 | PASS  | —           |
|  15 | Foundgine    |        3 | PASS  | —           |
|  15 | Foundgine    |        4 | PASS  | —           |
|  15 | Foundgine    |        5 | PASS  | —           |
|  15 | Foundgine    |        6 | PASS  | —           |
|  15 | Foundgine    |        7 | PASS  | —           |
|  15 | Foundgine    |        8 | PASS  | —           |
|  16 | Foundgine    |        1 | PASS  | —           |
|  16 | Foundgine    |        2 | PASS  | —           |
|  16 | Foundgine    |        3 | PASS  | —           |
|  16 | Foundgine    |        4 | PASS  | —           |
|  16 | Foundgine    |        5 | PASS  | —           |
|  16 | Foundgine    |        6 | PASS  | —           |
|  16 | Foundgine    |        7 | PASS  | —           |
|  16 | Foundgine    |        8 | PASS  | —           |
|  17 | Foundgine    |        1 | PASS  | —           |
|  17 | Foundgine    |        2 | PASS  | —           |
|  17 | Foundgine    |        3 | PASS  | —           |
|  17 | Foundgine    |        4 | PASS  | —           |
|  17 | Foundgine    |        5 | PASS  | —           |
|  17 | Foundgine    |        6 | PASS  | —           |
|  17 | Foundgine    |        7 | PASS  | —           |
|  17 | Foundgine    |        8 | PASS  | —           |
|  18 | Foundgine    |        1 | PASS  | —           |
|  18 | Foundgine    |        2 | PASS  | —           |
|  18 | Foundgine    |        3 | PASS  | —           |
|  18 | Foundgine    |        4 | PASS  | —           |
|  18 | Foundgine    |        5 | PASS  | —           |
|  18 | Foundgine    |        6 | PASS  | —           |
|  18 | Foundgine    |        7 | PASS  | —           |
|  18 | Foundgine    |        8 | PASS  | —           |
|  19 | Foundgine    |        1 | PASS  | —           |
|  19 | Foundgine    |        2 | PASS  | —           |
|  19 | Foundgine    |        3 | PASS  | —           |
|  19 | Foundgine    |        4 | PASS  | —           |
|  19 | Foundgine    |        5 | PASS  | —           |
|  19 | Foundgine    |        6 | PASS  | —           |
|  19 | Foundgine    |        7 | PASS  | —           |
|  19 | Foundgine    |        8 | PASS  | —           |
|  20 | Foundgine    |        1 | PASS  | —           |
|  20 | Foundgine    |        2 | PASS  | —           |
|  20 | Foundgine    |        3 | PASS  | —           |
|  20 | Foundgine    |        4 | PASS  | —           |
|  20 | Foundgine    |        5 | PASS  | —           |
|  20 | Foundgine    |        6 | PASS  | —           |
|  20 | Foundgine    |        7 | PASS  | —           |
|  20 | Foundgine    |        8 | PASS  | —           |
|  21 | Foundgine    |        1 | PASS  | —           |
|  21 | Foundgine    |        2 | PASS  | —           |
|  21 | Foundgine    |        3 | PASS  | —           |
|  21 | Foundgine    |        4 | PASS  | —           |
|  21 | Foundgine    |        5 | PASS  | —           |
|  21 | Foundgine    |        6 | PASS  | —           |
|  21 | Foundgine    |        7 | PASS  | —           |
|  21 | Foundgine    |        8 | PASS  | —           |
|  22 | Foundgine    |        1 | PASS  | —           |
|  22 | Foundgine    |        2 | PASS  | —           |
|  22 | Foundgine    |        3 | PASS  | —           |
|  22 | Foundgine    |        4 | PASS  | —           |
|  22 | Foundgine    |        5 | PASS  | —           |
|  22 | Foundgine    |        6 | PASS  | —           |
|  22 | Foundgine    |        7 | PASS  | —           |
|  22 | Foundgine    |        8 | PASS  | —           |
|  23 | Foundgine    |        1 | PASS  | —           |
|  23 | Foundgine    |        2 | PASS  | —           |
|  23 | Foundgine    |        3 | PASS  | —           |
|  23 | Foundgine    |        4 | PASS  | —           |
|  23 | Foundgine    |        5 | PASS  | —           |
|  23 | Foundgine    |        6 | PASS  | —           |
|  23 | Foundgine    |        7 | PASS  | —           |
|  23 | Foundgine    |        8 | PASS  | —           |
|  24 | Foundgine    |        1 | PASS  | —           |
|  24 | Foundgine    |        2 | PASS  | —           |
|  24 | Foundgine    |        3 | PASS  | —           |
|  24 | Foundgine    |        4 | PASS  | —           |
|  24 | Foundgine    |        5 | PASS  | —           |
|  24 | Foundgine    |        6 | PASS  | —           |
|  24 | Foundgine    |        7 | PASS  | —           |
|  24 | Foundgine    |        8 | PASS  | —           |
|  25 | Foundgine    |        1 | PASS  | —           |
|  25 | Foundgine    |        2 | PASS  | —           |
|  25 | Foundgine    |        3 | PASS  | —           |
|  25 | Foundgine    |        4 | PASS  | —           |
|  25 | Foundgine    |        5 | PASS  | —           |
|  25 | Foundgine    |        6 | PASS  | —           |
|  25 | Foundgine    |        7 | PASS  | —           |
|  25 | Foundgine    |        8 | PASS  | —           |
|  26 | Foundgine    |        1 | PASS  | —           |
|  26 | Foundgine    |        2 | PASS  | —           |
|  26 | Foundgine    |        3 | PASS  | —           |
|  26 | Foundgine    |        4 | PASS  | —           |
|  26 | Foundgine    |        5 | PASS  | —           |
|  26 | Foundgine    |        6 | PASS  | —           |
|  26 | Foundgine    |        7 | PASS  | —           |
|  26 | Foundgine    |        8 | PASS  | —           |
|  27 | Foundgine    |        1 | PASS  | —           |
|  27 | Foundgine    |        2 | PASS  | —           |
|  27 | Foundgine    |        3 | PASS  | —           |
|  27 | Foundgine    |        4 | PASS  | —           |
|  27 | Foundgine    |        5 | PASS  | —           |
|  27 | Foundgine    |        6 | PASS  | —           |
|  27 | Foundgine    |        7 | PASS  | —           |
|  27 | Foundgine    |        8 | PASS  | —           |
|  28 | Foundgine    |        1 | PASS  | —           |
|  28 | Foundgine    |        2 | PASS  | —           |
|  28 | Foundgine    |        3 | PASS  | —           |
|  28 | Foundgine    |        4 | PASS  | —           |
|  28 | Foundgine    |        5 | PASS  | —           |
|  28 | Foundgine    |        6 | PASS  | —           |
|  28 | Foundgine    |        7 | PASS  | —           |
|  28 | Foundgine    |        8 | PASS  | —           |
|  29 | Foundgine    |        1 | PASS  | —           |
|  29 | Foundgine    |        2 | PASS  | —           |
|  29 | Foundgine    |        3 | PASS  | —           |
|  29 | Foundgine    |        4 | PASS  | —           |
|  29 | Foundgine    |        5 | PASS  | —           |
|  29 | Foundgine    |        6 | PASS  | —           |
|  29 | Foundgine    |        7 | PASS  | —           |
|  29 | Foundgine    |        8 | PASS  | —           |
|  30 | Foundgine    |        1 | PASS  | —           |
|  30 | Foundgine    |        2 | PASS  | —           |
|  30 | Foundgine    |        3 | PASS  | —           |
|  30 | Foundgine    |        4 | PASS  | —           |
|  30 | Foundgine    |        5 | PASS  | —           |
|  30 | Foundgine    |        6 | PASS  | —           |
|  30 | Foundgine    |        7 | PASS  | —           |
|  30 | Foundgine    |        8 | PASS  | —           |

## Trace interpretation

The estimated context-load metric is intentionally narrower than a model's true prompt-token count. It measures the
recorded application tool payloads plus the fixed system/request overhead specified by the benchmark methodology. It is
useful for comparing how much tool-result context each architecture forces through the agent loop; it is not a
replacement for provider-reported usage.
