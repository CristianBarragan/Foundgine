# Production-readiness plan (checkpoint 5, WIP)

Goal: a verdict that can only say PRODUCTION GRADE when every criterion in `readiness/criteria.json` has recorded evidence.

1. **Entry gate (EG1-EG6)**: builds and tests pass with a measured count, zero skipped; adapters ran against real Redis/PostgreSQL; e2e example prints E2E_OK; harness samplers ran for real; CI Postgres version matches the parity test.
2. **Harness validity (R1-R3)**: A/A run, injected-regression gate test, loader calibration.
3. **Performance (R4-R6)**, **Correctness (R7-R10)**, **Endurance (R11)**, **Security (R12-R16)**, **Operability (R17-R21)**.
4. **Independence (IND1-IND3)**: second-party rerun, independent security review, optional pilot.
5. Each criterion ends as `pass`, `fail`, `accepted_gap` (needs owner + reason) or `not_run`. `not_run` never counts as pass.
6. **Verdict**: PRODUCTION GRADE = entry gate + all rows pass + required independence. CONDITIONALLY READY = entry gate, all critical/must rows pass, remaining rows pass or are accepted gaps. Anything else = NOT READY.

Run: `bash readiness/run.sh --stage selftest --stage calibrate --stage verdict` (no Docker needed); full list in `readiness/run.sh`.
