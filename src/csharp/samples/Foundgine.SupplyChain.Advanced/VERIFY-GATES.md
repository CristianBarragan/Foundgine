# Foundgine Verification Gates

The Supply Chain E2E is the **application-level story**. It sits on top of the repository's broader verification system. A release-quality Foundgine change is not considered complete because one benchmark passes; it must preserve the unit, PostgreSQL integration, security, adversarial-input and performance gates.

## Required gates

| Gate | What it covers | Current workflow/job |
| --- | --- | --- |
| .NET unit tests | Semantic, planning, authorization, MCP, AOT, InMemory and deterministic behavior | `csharp-build.yml` — `unit-tests` |
| Supply Chain sample tests | Starter and advanced sample behavior | `csharp-build.yml` — `supplychain-sample-tests`, `supplychain-semantic-sample-tests` |
| PostgreSQL integration | E2E and EventStore projects with PostgreSQL 17 available | `csharp-build.yml` — `integration-tests`; note the `PostgreSqlFact` environment-variable gap in [`docs/POSTGRES-E2E.md`](../../../../docs/POSTGRES-E2E.md) |
| Performance smoke tests | Hot Chocolate and Foundgine benchmark workloads | `csharp-build.yml` — `benchmark-build-hotchocolate`, `benchmark-build-foundgine` |
| Security checks | Dependency audit, SBOM, ZAP baseline, penetration and adversarial suites | `security.yml`, invoked by `csharp-build.yml` — `security-gate`; the Supply Chain GraphQL/MCP PenTest jobs currently skip their tests because the opt-in environment variable is missing |

There is no `agent-benchmark-smoke` job in the current workflows. Agent benchmark runs are separate workloads and must not be described as a required NuGet release gate.

The NuGet publication job runs only for version tags. Its `needs` list requires `build-pack`, `unit-tests`, both Supply Chain sample-test jobs, `integration-tests`, both benchmark jobs and `security-gate`. The security gate calls the reusable `security.yml` workflow; its individual jobs are not all jobs in `csharp-build.yml`.

## Local verification

From the repository root:

```powershell
dotnet restore src/csharp/Foundgine.sln
dotnet build src/csharp/Foundgine.sln -c Release --no-restore
dotnet test src/csharp/Foundgine.sln -c Release --no-build --filter "FullyQualifiedName!~Foundgine.E2E.Tests"
```

Then run the PostgreSQL-backed integration suite with the repository's PostgreSQL compose environment:

```powershell
docker compose -f docker-compose.postgres.yml up -d --wait postgres
$env:FOUNDGINE_POSTGRES_CONNECTION_STRING='Host=localhost;Port=55432;Database=foundgine_e2e;Username=foundgine;Password=foundgine'
dotnet test src/csharp/tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Foundgine.E2E.Tests"
docker compose -f docker-compose.postgres.yml down --volumes --remove-orphans
```

Authorization penetration coverage:

```powershell
$env:FOUNDGINE_POSTGRES_CONNECTION='Host=localhost;Port=55432;Database=foundgine_e2e;Username=foundgine;Password=foundgine'
dotnet test src/csharp/tests/Foundgine.Security.Authority.Tests/Foundgine.Security.Authority.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~TransferFundsPenetrationTests'
```

Adversarial semantic-input coverage:

```powershell
dotnet test src/csharp/tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~ModelProviderReplayTests.Hostile_model_corpus_is_replayed_through_the_real_engine'
dotnet test src/csharp/tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~BlackBoxAdversarialEngineTests'
```

The full performance smoke tests are defined in `.github/workflows/csharp-build.yml`; the larger CoffeeBeanery performance matrices remain benchmark workloads rather than release-gate tests.

## Interpreting "passing"

A benchmark result is not a security pass merely because the process exits successfully. For the Supply Chain workload, the report must also show that expected-deny operations remain denied and that denied operations do not mutate protected state.

Likewise, a performance run is a smoke-test pass when the workload completes without application errors/timeouts/cancellations; it is not a claim that Foundgine is universally faster than another stack.
