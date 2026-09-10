# Foundgine Verification Gates

The Supply Chain E2E is the **application-level story**. It sits on top of the repository's broader verification system. A release-quality Foundgine change is not considered complete because one benchmark passes; it must preserve the unit, PostgreSQL integration, security, adversarial-input and performance gates.

## Required gates

| Gate | What it proves | CI job / command |
|---|---|---|
| Unit tests | Semantic, planning, authorization, MCP, AOT, InMemory and other deterministic behavior | `unit-tests` |
| PostgreSQL integration tests | Real provider behavior and end-to-end database semantics | `integration-tests` |
| Authorization penetration tests | High-assurance authorization cannot be bypassed through the real PostgreSQL path | `security-penetration` |
| Adversarial semantic-input tests | Hostile model input and replay/corpus cases remain inside the semantic security boundary | `security-adversarial` |
| Performance smoke test | The benchmark stack can seed, start, execute real traffic and finish without errors | `benchmark-build-hotchocolate`, `benchmark-build-foundgine` |
| Agent benchmark smoke + Supply Chain E2E | Run1-5 agent benchmark pipeline, plus the stateful agent → MCP → Foundgine → PostgreSQL business workflow | `agent-benchmark-smoke` |

The GitHub Actions release gate requires the unit, integration, authorization penetration, adversarial security, performance and agent-benchmark-smoke jobs (which now also runs the Supply Chain E2E benchmark on every push/PR) before NuGet publication.

## Local verification

From the repository root:

```powershell
dotnet restore Foundgine.sln
dotnet build Foundgine.sln -c Release --no-restore
dotnet test Foundgine.sln -c Release --no-build --filter "FullyQualifiedName!~Foundgine.E2E.Tests"
```

Then run the PostgreSQL-backed integration suite with the repository's PostgreSQL compose environment:

```powershell
docker compose -f docker-compose.postgres.yml up -d --wait postgres
dotnet test tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Foundgine.E2E.Tests"
docker compose -f docker-compose.postgres.yml down --volumes --remove-orphans
```

Authorization penetration coverage:

```powershell
$env:FOUNDGINE_POSTGRES_CONNECTION='Host=localhost;Port=55432;Database=foundgine_e2e;Username=foundgine;Password=foundgine'
dotnet test tests/Foundgine.Security.Authority.Tests/Foundgine.Security.Authority.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~TransferFundsPenetrationTests'
```

Adversarial semantic-input coverage:

```powershell
dotnet test tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~ModelProviderReplayTests.Hostile_model_corpus_is_replayed_through_the_real_engine'
dotnet test tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~BlackBoxAdversarialEngineTests'
```

The full performance smoke test is defined in `.github/workflows/build.yml`; the larger CoffeeBeanery performance matrices remain benchmark workloads rather than release-gate tests.

## Interpreting "passing"

A benchmark result is not a security pass merely because the process exits successfully. For the Supply Chain workload, the report must also show that expected-deny operations remain denied and that denied operations do not mutate protected state.

Likewise, a performance run is a smoke-test pass when the workload completes without application errors/timeouts/cancellations; it is not a claim that Foundgine is universally faster than another stack.
