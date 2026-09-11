# Agent Benchmark Expected Behavior

The benchmark does not determine correctness by comparing Conventional and Foundgine serialized snapshots to each other.

Before each benchmark suite, the harness resets the benchmark customers and captures a baseline snapshot for each customer. It then derives an explicit state machine:

1. `CustomerId` must remain unchanged.
2. `CustomerKey` must remain unchanged.
3. Relationship count must remain unchanged.
4. Contract count must remain unchanged.
5. Transaction count must remain unchanged.
6. Exposure must remain unchanged.
7. If exposure is at least `48,000`, QUERY #1 must observe the baseline exposure and MUTATION #1 changes `FullName` to `Customer {id} Benchmark | Reviewed`.
8. QUERY #2 must observe that reviewed intermediate state before MUTATION #2 is allowed to continue.
9. MUTATION #2 changes `FullName` to `Customer {id} Benchmark | Reviewed | Remediation Complete`.
10. QUERY #3 must observe that final state.
11. No other snapshot field may change.

The benchmark writes the generated expectations to:

`artifacts/.../expected-state.json`

Each measured flow is compared against the expected state for its own customer. The report contains the detailed field-level verification results.
