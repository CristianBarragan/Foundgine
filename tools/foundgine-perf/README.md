# foundgine-perf (checkpoint 1)

Benchmark harness that closes the gaps listed in *Foundgine Performance Report* (16 Nov 2026).
Stdlib-only Python 3.10+, so it runs anywhere the targets are reachable over HTTP (GraphQL, MCP-over-HTTP, REST).

## What is in this checkpoint (built and self-tested)
| Report action | Where | Status |
|---|---|---|
| 1 Resource telemetry, no placeholder zeros | `fpbench/sampler.py` (pid, cgroup v2, docker; runs on the host). A cell with zero samples is marked invalid | pid mode verified; docker/cgroup modes written, NOT yet run (no Docker here) |
| 2 Response equivalence | `loadgen.canonical` + runner probe: normalised JSON hash per request, mismatch marks the scenario non-equivalent | verified incl. negative test |
| 3 >=5 sessions, randomised interleaved order, stable-throughput warm-up, median ratio with 95% bootstrap CI | `runner.py`, `stats.py`, `report.py` | verified (3 sessions in selftest) |
| 4 Environment disclosure | `env.py` -> `environment.json`; report blocks claim-grade if incomplete | verified |
| 5 Open loop, latency from intended send time | `loadgen._open_loop`, fractions of measured capacity | verified on mocks; knee search past 64 not yet scripted |
| Little's Law gate | `runner.py` marks cells that disagree by >15% | verified |

`report.md` states **Claim grade: NO** with explicit blockers unless every gate passes.

## Checkpoint 2 additions
- `scenarios/gen_coffeebeanery.py` + generated `cb-*.json`: exact GraphQL operations from `CoffeeBeanery.LoadTest` (query top-50, upsert batch 1/10/50; logical ops/s).
- `configs/coffeebeanery.json`, `compose/perf.override.yml`: published ports, equal CPU/memory limits and cpusets, loader on the host. **Written, not run (no Docker here).**
- `convention/footprint.py` (run on the real repo, results in `docs/CONVENTION-TRACK.md`), `convention/startup_meter.py` (tested on a mock only), `docs/CONVENTION-TRACK.md` (three-arm design).

## Checkpoint 3 additions
- `fpbench/journey.py`, `fpbench/journey_report.py`, `fpbench/journeys/foundgine_e2e.py`: open-loop business journeys (latency from intended arrival), step timing, probes, fault-injection events, sustainable-rate report.
- `configs/e2e-journeys.json`, `docs/E2E-JOURNEYS.md`: plan from report section 5 mapped to runnable cells. `selftest/run_selftest_journeys.sh` verifies the runner on a mock stack. **The e2e example itself now builds and passes EG4 (E2E_OK and the Temporal CustomerToCash workflow Completed), but only after `readiness/prepare-e2e.sh` patches a copy of it (ten fixes, listed in that script's header: Foundgine version pin, split Extensions packages, Flyway starter, missing `Directory.Build.props` in the CRM images, `/dev/tcp` health probes under dash, the one-shot `erp-seed` in `up --wait`, Keycloak realm without the `basic` scope so tokens had no `sub`). The journeys and configs below have not been run against it yet.**

## Checkpoint 4 additions
- `tests/test_fpbench.py` (11 unit tests: percentiles, bootstrap, Little's Law, canonical hashing, sampler zero-sample rule, gate, report grading), file-handle leak fixes in the sampler.
- `fpbench/gate.py` + `baselines/`: regression gate (`python3 -m fpbench gate new.json baseline.json --require-claim-grade`).
- `.github/workflows/perf.yml`: harness tests + footprint on every PR; library benchmark, e2e journeys and AOT jobs on a dedicated self-hosted runner. **Written, never executed in CI.**
- `docs/WHY-FOUNDGINE.md`: claims register with evidence status per claim.

## Not done yet (next increments)
- Real scenarios for CoffeeBeanery (Hot Chocolate+EF vs Foundgine) and compose files with CPU/memory limits and the loader on pinned cores.
- Convention/AOT track ("why Foundgine"). Finding from the repo: `Foundgine.CoffeeBeanery.BenchmarkApi` hand-writes `CoffeeBeanerySemanticModel.cs` and `CoffeeBeaneryMetadata.cs`; it does not use `Foundgine.Providers.Orm.Generator` (`[FoundgineEntity]` attributes). So current numbers do not exercise the convention path. Needed: a generator-based variant of the API plus scaffolding metrics (hand-written vs generated lines, time to first endpoint, AOT cold start, binary size, RSS).
- Temporal schedule-to-start and duplicate-delivery probes, CRM outbox probe.
- Committed claim-grade baseline for the gate (needs a real run).
- Equal-work controls (real auth policy, tuned EF, raw SQL floor, EF batch tool), outlier investigation, e2e journey tests, CI workflow.

## Honest limits
No .NET SDK or Docker in the authoring sandbox, so nothing C# was compiled or run. Selftest numbers come from Python mock servers on a 1-core sandbox and are not Foundgine performance.

## Run the selftest
`bash selftest/run_selftest.sh`

## Root cause of the zero CPU/memory in existing reports
`CoffeeBeanery.LoadTest/Program.cs` calls `docker stats` from inside the load-generator container, which has no Docker CLI/socket, so every read returns null and averages default to 0.

## Checkpoint 5 additions (WIP)
- `readiness/`: `criteria.json` (R1-R21, EG1-EG6, IND1-3), `run.sh` + `stages/*.sh`, `gen_configs.py` (all e2e configs from one compose project name, 31 fault cells), `parse_results.py` (TRX/Surefire counts, skipped = reject), `prepare-e2e.sh` (patched copy of the e2e example that builds against the checkout under test), `lib.sh`.
- `fpbench/readiness.py` (evidence ledger + verdict), `fpbench/checks.py` (aa, soak, faults, tenant, calibrate, perf, claim, restart), `fpbench/invariants.py` (cross-system data invariants).
- Tests: 30 unit tests incl. `tests/test_readiness.py`.
- Verified here (no Docker/.NET/Java): unit tests, `selftest`, `calibrate`, bash syntax of every stage. **Written but never run: entry, e2e-up, lib-aa, lib, e2e-perf, faults, soak, tenant, ops, existing-suites, scans.**
- Fixed vs the earlier session: partition cells reconnect with `--alias <service>` so peers can still resolve the service name.
- Checkpoint 6: staged-rate cells (`"staged": [{rate, s}, ...]` in journey configs), `fpbench check recovery` (R6 recovery time + graceful degradation), `e2e-staged.json`; verified on the mock stack.
- Still open: equal-work controls (R5), cross-surface auth parity and Redis-down live tests (R13/R14), agent attack corpus (R15), EG6/R20/R21/IND1/IND2 are manual.
- Checkpoint 7: `fpbench/parity.py` + `check parity` + `readiness/stages/parity.sh` + `readiness/parity-vectors.json` (R13). Data-driven: fill the template with real REST/GraphQL/MCP requests (>= 10 vectors, 3 surfaces, no `todo`). Verified on the mock only for the checker itself (it passes clean and catches an injected tenant leak); the mock serves all three "surfaces" from one endpoint, so this is NOT evidence of parity. A bug found and fixed while testing: surface labels without an explicit `base` crashed.
- Checkpoint 8: new criterion **R22** (critical): bearer-token hardening. `fpbench/authattack.py`, `check authattack`, `readiness/stages/auth-attack.sh` (needs `AUTH_ENDPOINTS`). Sends no token, garbage, bad signature, `alg: none` and edited-payload tokens to each protected endpoint; all must be 401/403, and a valid token must still work (positive control). 5xx/timeouts are inconclusive, never a pass. Verified against a local strict server (pass) and a permissive one (fail); not run against the real stack. It does not cover approval tokens/warrants (R14), which are separate.
- Checkpoint 9: `readiness/stages/preflight.sh`. Run `bash readiness/run.sh --stage preflight` first on the real host: checks tools (docker daemon, dotnet, mvn, java, git, python >= 3.10), checkouts, the env vars each stage needs, free ports, >= 20 GB disk, and runs shellcheck if installed. `STAGES="faults soak"` limits it to what you plan to run. Writes no evidence. Tested here: it correctly reports the sandbox's missing tools and low disk.
## Checkpoint 11: ORM pairs (step 1 of 2, C#)
New arm `foundgine-ef` (Foundgine GraphQL + Foundgine EF Core provider over the same `BankingEntityContext` as Hot Chocolate + EF Core), wired into `compose/query.yml`, `compose/perf.override.yml` and `configs/coffeebeanery.json`. See `docs/ORM-PAIRS.md`. **Not compiled or run**; `tests/test_orm_pairs.py` checks wiring only. Java pair (Spring GraphQL + Hibernate vs Foundgine + JPA provider) is step 2.
