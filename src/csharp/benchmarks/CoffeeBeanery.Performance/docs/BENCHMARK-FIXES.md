# Benchmark fixes applied

The first benchmark run is treated as a correctness signal, not as a performance result. The observed output contained successful Foundgine query numbers at concurrency 1 but zero-RPS/error rows at higher concurrency, while the Hot Chocolate baseline returned GraphQL execution errors. Those results should not be used to claim a performance advantage.

## Fixes in this package

### 1. Hot Chocolate baseline is a real, self-contained benchmark API

The benchmark project explicitly uses:

- Hot Chocolate ASP.NET Core `15.1.16`
- EF Core `9.0.4`
- Npgsql EF Core provider `9.0.4`
- the same `CoffeeBeanery.Database` project and PostgreSQL fixture as Foundgine

Hot Chocolate exception details are enabled because this project is a benchmark/diagnostic application. This makes `Unexpected Execution Error` failures actionable instead of hiding the underlying exception.

### 2. Npgsql 9 package is pinned to an actually available 9.x release

The active benchmark projects use Npgsql EF Core `9.0.4`. The earlier `9.0.7` reference was not available from the package feed used by the benchmark machine, which caused NuGet to fall forward to Npgsql EF Core `10.0.0` and fail against `net9.0`.

### 3. Database readiness is part of container health

Both API services expose `/health/ready`. The readiness endpoint verifies that PostgreSQL can actually be reached instead of merely proving that ASP.NET Core has started.

### 4. Correctness preflight happens before performance measurement

The loader executes one representative query and one representative mutation against every target before the first measurement. HTTP failures, malformed JSON, and GraphQL `errors` are reported as failures.

The benchmark never stops the full matrix because one case fails; failed cases are retained as diagnostics.

### 5. Timeouts are explicit

The loader uses a configurable per-request timeout (`BENCHMARK_REQUEST_TIMEOUT_SECONDS`, default `10`). Request timeouts are reported separately from application errors.

### 6. Benchmark reports distinguish invalid runs from valid measurements

Reports now include completed requests, errors, timeouts, elapsed time, status, and the first observed error. A performance row is only valid when it has zero errors and zero timeouts.

### 7. SQL schema qualification is tested

Foundgine storage names such as `Banking.Customer` are rendered as two identifiers:

`"Banking"."Customer"`

rather than one identifier:

`"Banking.Customer"`

A regression test (`M41StorageNameQuotingTests`) protects this behavior.

### 8. Benchmark projects are included in the root solution

The four benchmark projects are now part of `Foundgine.sln`, so IDE restore/build can resolve the project references without treating the benchmark API as an unrelated unloaded project.

### 9. Stale build output and duplicate benchmark source were removed

The package contains no `.git`, `bin`, `obj`, `.idea`, or duplicated `src/csharp/benchmarks/benchmarks` / `src/csharp/benchmarks/src` trees. A root `.dockerignore` also prevents stale build artifacts and historical reports from entering Docker build contexts.


## v2 benchmark readiness and cache hardening

- Target readiness has a separate 90-second startup timeout; this is not the per-request timeout.
- Readiness failures now include the last HTTP status/body or exception.
- The built-in provider-plan memory cache now uses single-flight compilation for concurrent misses.
- Cache hits remain process-local and bounded. Semantic resolution and authorization still occur on every request.
- The warm benchmark caches only the compiled provider plan for the read workload. Mutation compilation remains per request because mutation values are dynamic.
