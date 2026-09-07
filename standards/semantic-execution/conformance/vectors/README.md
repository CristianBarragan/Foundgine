# Portable conformance vectors (SES-105)

Status: **partial** — starter set, not full coverage. Tracked as `SES-GAP-05` in
[`../known-gaps.json`](../known-gaps.json).

Each `VEC-*.json` file follows the vector model defined in
[SES-105 §2](../../SES-105-conformance-vectors.md#2-vector-model):
`id`, `standard_version`, `profile`, `contract`, `intent`, `trusted_context`,
`expected_graph`, `expected_authorization`, `expected_provenance`,
`expected_plan_properties`, `expected_result_or_error`, `resource_budget` —
plus `invariant_under_test` and `negative_behavior_note`, which SES-105 §4–§5
require every vector to state explicitly.

## Coverage against SES-105 §3's mandatory future vector families

| Family                          | Vector                          | Status  |
| -------------------------------- | -------------------------------- | ------- |
| authorized read                  | VEC-001-authorized-read          | Done    |
| unknown capability                | VEC-002-unknown-capability       | Done    |
| ambiguous resolution              | VEC-003-ambiguous-resolution     | Done    |
| cross-tenant read                 | VEC-004-cross-tenant-read        | Done    |
| cross-tenant mutation             | VEC-005-cross-tenant-mutation    | Done    |
| traversal escape                  | VEC-006-traversal-escape         | Done    |
| predicate removal                 | —                                | Missing |
| stale provenance                  | VEC-007-stale-provenance         | Done    |
| cache poisoning                   | —                                | Missing |
| prompt injection                  | VEC-008-prompt-injection         | Done    |
| graph explosion                   | —                                | Missing |
| provider capability mismatch      | —                                | Missing |
| mutation atomicity failure         | —                                | Missing |
| mutation replay                   | —                                | Missing |
| concurrency conflict              | —                                | Missing |
| optimized/reference equivalence   | —                                | Missing |
| transport equivalence             | —                                | Missing |
| generated/runtime parity          | —                                | Missing |
| temporal semantics                | —                                | Missing |
| null/duplicate/order semantics    | —                                | Missing |

8 of 20 mandatory families have a vector. The remaining 12 are unstarted
backlog, not silently skipped — each corresponds to an open item and should be
picked up before `SES-GAP-05`/`SES-VEC-001` is marked `proven` rather than
`partial`.
