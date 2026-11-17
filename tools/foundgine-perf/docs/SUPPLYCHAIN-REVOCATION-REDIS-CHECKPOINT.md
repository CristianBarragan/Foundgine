# SupplyChain benchmark — shared Redis revocation checkpoint

This change brings the Java Checkpoint 8 revocation model into the four SupplyChain benchmark arms and gives the Hot Chocolate baseline the same Redis/PostgreSQL security-state dependency as the Foundgine arm.

## Runtime pairs

| Pair | Foundgine arm | Plain baseline |
|---|---|---|
| C# | `RedisSecurityWarrantRevocationStore` + `AnchoredSecurityWarrantRevocationStore` + `PostgresSecurityWarrantRevocationEpochAnchor` | Direct StackExchange.Redis + Npgsql implementation in `BaselineRevocationEpochGuard` |
| Java | `RedisSecurityWarrantRevocationStore` + `AnchoredSecurityWarrantRevocationStore` + `PostgresSecurityWarrantRevocationEpochAnchor` | Direct Lettuce + JDBC implementation in `BaselineRevocationEpochGuard` |

The baselines do not reference Foundgine provider assemblies. They implement the same Redis key layout, identity/digest lookups, monotonic sequence bracketing, PostgreSQL `GREATEST` high-water update, synchronous commit, primary check, and read-after-write verification using ordinary client APIs.

## Redis topology and isolation

- One Redis 7.4 service is shared by all four arms.
- Redis AOF is enabled with `appendfsync=always` and a persistent named volume.
- C# baseline and Foundgine use `benchmark:csharp:{warrant-revocations}:...` keys.
- Java baseline and Foundgine use `benchmark:java:{warrant-revocations}:...` keys.
- The Redis container has the same resource limits for every run and is included in every target's Docker resource sample.
- Each arm retains its own PostgreSQL database and durable high-water table.

## Fail-closed behavior

Every authenticated GraphQL request checks both the identity and digest revocation keys and brackets those lookups with sequence reads. The Redis sequence may never be lower than the PostgreSQL high-water mark. Each synchronization atomically advances the durable mark with `GREATEST`, commits with `synchronous_commit=on`, and verifies the committed value on a fresh read. A Redis rollback, PostgreSQL standby, missing anchor row, read-after-write mismatch, or service outage rejects the request. `/health/ready` also performs a bounded Redis write/read/delete probe.

The PostgreSQL anchor table is benchmark security infrastructure. Foundgine arms create it through the provider migrator; baseline arms create the same table with their direct infrastructure initializer/Flyway migration. It is included in schema parity and must have identical structure within each runtime pair.

## Build and run gates

Run from the repository root:

```bash
python3 -m unittest tools/foundgine-perf.tests.test_supplychain_wiring \
  tools.foundgine-perf.tests.test_java_benchmark_apps -v

dotnet build src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi/HotChocolate.SupplyChain.BenchmarkApi.csproj
dotnet build src/csharp/benchmarks/SupplyChain.Performance/Foundgine.SupplyChain.BenchmarkApi/Foundgine.SupplyChain.BenchmarkApi.csproj
```

Run from `src/java` with JDK 27 and Maven 3.9.15:

```bash
mvn -pl benchmarks/supply-chain/spring-baseline,benchmarks/supply-chain/foundgine -am verify
```

Then from `tools/foundgine-perf`:

```bash
docker compose -f compose/keycloak.yml -f compose/supplychain.yml -f compose/supplychain.perf.yml up -d --build
curl -f http://127.0.0.1:4410/health/ready
curl -f http://127.0.0.1:4411/health/ready
curl -f http://127.0.0.1:4420/health/ready
curl -f http://127.0.0.1:4421/health/ready
python3 -m fpbench schemaparity configs/supplychain-csharp.json --out out/sc-cs
python3 -m fpbench schemaparity configs/supplychain-java.json --out out/sc-java
```

Only after both schema-parity gates pass should you run `armauthz`, reseed after its mutation probes, and then run the performance cells.

## Verification performed for this checkpoint

- SupplyChain wiring tests: 8 passed.
- Java benchmark application contract tests: 5 passed.
- Compose YAML, benchmark JSON configs, C# project XML and Java Maven POMs parse successfully.
- Full performance harness suite currently has unrelated pre-existing CoffeeBeanery failures: its expected `Foundgine.EfCore.CoffeeBeanery.BenchmarkApi` project is absent and it expects a 4303 service not present in that compose file. Those failures are outside this SupplyChain change.
- .NET SDK, Maven and Docker are unavailable in the implementation environment, so no C#/Java compile, live Redis/PostgreSQL test, Keycloak validation or benchmark measurement is claimed.
