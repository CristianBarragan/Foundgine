# E2E journey benchmarks (Foundgine-e2e-example)

Implements the report's Section 5 plan. Run with `python3 -m fpbench journeys configs/e2e-journeys.json --out results/e2e`
then `python3 -m fpbench journey-report results/e2e --slo-p99-s <SLO>`.

| Report journey | Implementation | Load shape | Metrics |
|---|---|---|---|
| Create customer -> ERP onboarding completes | `customer_onboarding`: quote acceptance only succeeds once the customer mapping is Active, so the wait is the sync time | open loop 5/20/50 per s | p50/p95/p99 from intended arrival, `wait.erp_customer_sync` step |
| Quote -> order -> shipment -> invoice -> payment -> CRM projection | `customer_to_cash` mirrors `e2e/run.sh` | open loop 1/5/10/20 per s; sustainable rate = highest rate with <=1% failures and >=98% completion (and p99 within the SLO if supplied) | step breakdown (`wait.quote_ordered`, `wait.crm_projection`, ERP calls), outbox backlog and oldest-unpublished age |
| Read paths | `read_paths` | closed loop 1/8/32 | latency, throughput |
| ERP outage and recovery | `customer-to-cash-erp-outage`: `docker pause`/`unpause` ERP at 45 s / 105 s | steady 5 per s | failures during fault, `time_to_drain_s`, outbox backlog max |
| Every journey | `resources` (docker stats on the host), `probes` | sampled every second | CPU, RSS, backlog; a declared source with zero samples marks the cell invalid |

## Not covered / caveats
- Temporal schedule-to-start latency and duplicate-delivery counts need the Temporal metrics endpoint and a DB check; not wired yet (add as `probes` commands).
- The CRM-side outbox (SQL Server) has no probe yet; the ERP outbox probe uses `erp_temporal_event_outbox` (published_at, created_at).
- Calls inside a journey are not retried, so an outage shows as failed journeys plus a drain time for journeys that survived; model client retry separately if you want to measure that.
- Fixture must be reset between sessions (new tenant or DB restore) so outbox/stock state does not drift; use `reset_cmd` per journey.
- Everything here was tested only against `selftest/mock_e2e.py`. The e2e example itself has never been built or run.
