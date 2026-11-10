# EF Core Native AOT validation

The .NET ORM AOT track targets EF Core. The smoke application executes Foundgine's generated entity map, tenant authorization, and selected-field projection through a real EF Core SQLite provider. It emits a success marker only after the semantic assertions complete. CI requires both a successful native process exit and that exact marker.

## Local validation

On a machine with the .NET 10 SDK and the native toolchain for the selected RID:

```sh
./aot/validate-aot.sh linux-x64
```

The CI matrix runs `linux-x64` and `linux-arm64` on matching architecture runners. Cross-publishing without native execution is not certification. Trimming/AOT warnings remain visible; no warning suppression is used to turn a failed publish into a pass.

## Warning ownership and baseline

The diagnostic report distinguishes Foundgine-owned warnings from dependency and unclassified warnings. Newly observed Foundgine-owned `IL2026` or `IL3050` warnings block certification. The reviewed baseline is fail-closed and is changed only through maintainer review. Generated-source locations are preserved; authored-source attribution is not guessed when compiler output lacks a mapping.

## Evidence and release gate

Each EF Core/RID record includes commit SHA, workflow run ID, run attempt, workflow reference, publish result, native runtime result, and warning-baseline result. The aggregator expects exactly `efcore/linux-x64` and `efcore/linux-arm64`; both must be present, provenance-valid, internally consistent, and certified. It also records SHA-256 digests and byte sizes for evidence files.

For non-pull-request runs, GitHub Actions attests the exact aggregate report using workload identity. The attestation is separate from the report's pass/fail decision: a signed report can still say `not-certified`. Verify a downloaded report with `gh attestation verify aggregate-report.json --repo OWNER/REPOSITORY`.

The checked-in matrix describes implementation and evidence status. It does not claim that CI has already run. Update certification claims only after reviewing actual workflow artifacts.
