# Foundgine ORM execution core (.NET)

The .NET ORM adapter is EF Core. The semantic execution core remains provider-neutral and owns plan validation, authorization provenance, projection, relationship execution shape, pagination contracts, and mutation safety. EF Core translates the accepted expression tree into database operations.

```text
Semantic Operation
  -> Authorization Binding
  -> Semantic Plan / ExecutionIR
  -> provider capability gate
  -> generated, strongly typed expression fragments
  -> EF Core IQueryable / Criteria translation
  -> database
```

## Boundaries

- The provider-neutral core does not reference an ORM package.
- Generated maps provide strongly typed entity/field bindings; runtime entity discovery is not the execution strategy.
- Authorization predicates are composed as mandatory narrowing predicates. Missing authorization context fails closed.
- Projection selects requested fields; relationship execution batches child keys and applies child authorization independently.
- Unsupported query shapes fail before database execution. A declared capability is not proof of translation or runtime conformance.
- Mutations validate the complete batch before writes, use one transaction boundary, preserve authorization filters, and roll back on failure.

## Capability status

The authoritative, machine-readable status is `src/csharp/conformance/orm/provider-conformance-matrix.json`. It separates implementation, SQL translation, runtime execution, Native AOT publish, and native execution evidence. `test-defined-not-run` is not a pass. Aggregate cursor pagination remains disabled until a real database test proves mixed aggregate/ordinary keyset boundaries, stable tie-breaking, and authorization on every page.

## Verification

Run `dotnet test src/csharp/tests/Foundgine.Orm.Tests/Foundgine.Orm.Tests.csproj -c Release`. Native AOT validation is described in `src/csharp/aot/README.md`. CI certification is scoped to native `linux-x64` and `linux-arm64` runs and requires publish, exact semantic success marker, warning-baseline, provenance, integrity, and attestation checks.
