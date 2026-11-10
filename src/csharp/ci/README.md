# ORM CI and conformance

The .NET ORM integration currently targets **EF Core**. The provider-neutral semantic and execution contracts remain separate from the EF Core adapter.

1. **Evidence matrix** — validates the checked-in capability/evidence manifest and AOT diagnostic tooling.
2. **Behavioural conformance** — runs the provider-neutral tests and real EF Core SQLite integration tests on Windows and Linux. Coverage includes tenant and row authorization, selected-field projection, filters, ordering, pagination, relationship traversal, provenance, and mutation rollback.
3. **Native AOT certification** — publishes and executes EF Core on native `linux-x64` and `linux-arm64` runners, checks the exact semantic success marker, and enforces the reviewed warning baseline.
4. **Evidence aggregation and attestation** — verifies run/commit/workflow provenance, lane completeness, content hashes, and the aggregate release gate; non-PR runs attest the exact aggregate report.

A passing source-level test or configured workflow is not evidence that a workflow has executed. The matrix must remain `test-defined-not-run` until CI artifacts prove otherwise. Aggregate cursor pagination remains disabled until translation and runtime tests pass.
