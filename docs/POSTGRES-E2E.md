# PostgreSQL 17 E2E

The repository has a real PostgreSQL 17 integration path.

The normal test suite does not require a database. The PostgreSQL tests run when `FOUNDGINE_POSTGRES_CONNECTION_STRING` is set.

CI always supplies that connection string.


## PostgreSQL retrieval in the canonical lifecycle

PostgreSQL retrieval is a branch inside the canonical semantic lifecycle, not a second pipeline:

![PlantUML diagram: POSTGRES-E2E, diagram 1](assets/postgres-e2e-plantuml-01.svg)

No retrieval strategy grants authority or bypasses semantic authorization.

## Start PostgreSQL

```bash
docker compose -f docker-compose.postgres.yml up -d
```

Check:

```bash
docker compose -f docker-compose.postgres.yml ps
```

Check the version:

```bash
docker compose -f docker-compose.postgres.yml exec -T postgres \
  psql -U foundgine -d foundgine_e2e -c 'select version();'
```

The container uses:

```text
image: pgvector/pgvector:pg17
host port: 55432
database: foundgine_e2e
user: foundgine
password: foundgine
```

`pgvector/pgvector:pg17` is a drop-in-compatible build of the official `postgres:17` image with the `pgvector` extension preinstalled. It is required by `Foundgine.Providers.Storage.PostgresVector`'s E2E tests (`PostgresVectorE2ETests`), which call `CREATE EXTENSION vector` — something a vanilla `postgres:17` image cannot satisfy.

The database uses a temporary filesystem, so it is safe to throw away after the test.

## Run the tests

PowerShell:

```powershell
$env:FOUNDGINE_POSTGRES_CONNECTION_STRING="Host=localhost;Port=55432;Database=foundgine_e2e;Username=foundgine;Password=foundgine"
dotnet test .\src\csharp\tests\Foundgine.E2E.Tests\Foundgine.E2E.Tests.csproj --configuration Release --filter "FullyQualifiedName~Postgres"
```

Bash:

```bash
export FOUNDGINE_POSTGRES_CONNECTION_STRING='Host=localhost;Port=55432;Database=foundgine_e2e;Username=foundgine;Password=foundgine'
dotnet test src/csharp/tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj --configuration Release --filter "FullyQualifiedName~Postgres"
```

One command:

```bash
bash ./scripts/run-postgres-e2e.sh
```

PowerShell:

```powershell
.\scripts\run-postgres-e2e.ps1
```

## What the PostgreSQL tests prove

The PostgreSQL tests are the physical database proof for:

- generated identity correlation;
- dependency levels;
- compiler-owned correlation;
- batched mutation execution;
- real PostgreSQL execution;
- the pgvector-backed lexical grounding pipeline — semantic contract → lexicon projection → embedding → pgvector index → nearest-neighbor retrieval → graph-constrained resolution (`PostgresVectorE2ETests`).

The tests that need a database are deliberately skipped when no connection string exists. This keeps ordinary local tests easy to run.

The dedicated PR job does not skip them.

## Clean up

```bash
docker compose -f docker-compose.postgres.yml down --volumes --remove-orphans
```

## Measurement gate

Do not optimize the PostgreSQL compiler before collecting real PostgreSQL measurements.

The target matrix is:

```text
batch size: 1 / 10 / 50 / 500
depth:      1 / 2 / 3
```

Collect:

```text
planning time
execution time
shared buffer hit/read/write
temporary read/write
WAL bytes
join type
sorts
materialization
estimated rows
actual rows
actual loops
```

The goal is to find the expensive part from the PostgreSQL plan instead of guessing.

---

Next: [Testing](TESTING.md)
