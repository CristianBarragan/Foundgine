# Stateful Agent Scenario — Query → Mutation → Query → Mutation → Query

## Scenario

Customer exposure remediation is executed as one stateful process.

1. **QUERY #1** — Traverse Customer → CustomerBankingRelationship → Contract → Transaction and calculate exposure.
2. **MUTATION #1** — If exposure >= 48,000, set `FullName` to `Customer {id} Benchmark | Reviewed`.
3. **QUERY #2** — Re-read the customer graph and verify the intermediate mutation.
4. **MUTATION #2** — Set `FullName` to `Customer {id} Benchmark | Reviewed | Remediation Complete`.
5. **QUERY #3** — Re-read the complete graph and verify the final state.

## Expected invariants

`CustomerId`, `CustomerKey`, relationship count, contract count, transaction count, and exposure must remain unchanged.

Only `FullName` may transition:

`Customer {id} Benchmark`
→ `Customer {id} Benchmark | Reviewed`
→ `Customer {id} Benchmark | Reviewed | Remediation Complete`

The benchmark's expected-state file records the baseline, intermediate, and final states.

## Concurrency

For concurrency 8/16/32/64, run with at least 64 isolated customers (recommended: 128) so customer IDs are not reused across concurrent workers.

Example:

```powershell
.\benchmarks\AgentEndToEnd\Run2\run-agent-end-to-end.ps1 `
    -CustomerCounts 128 `
    -RunsPerTier 30 `
    -Warmups 5 `
    -Concurrency 8,16,32,64
```

Replay mode uses the existing benchmark token estimator:

`max(chars / 4, words × 1.3)`

Provider-reported token usage remains authoritative in live mode.


## Runner diagnostic fix
The PowerShell runner captures dotnet stdout/stderr through `cmd.exe` into `agent-benchmark-console.log`. This prevents PowerShell native stderr handling from masking the actual benchmark exception. A non-zero dotnet exit now prints the last 40 log lines before cleanup.
