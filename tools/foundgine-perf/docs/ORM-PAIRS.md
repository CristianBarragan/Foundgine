# ORM pairs: like-for-like GraphQL + ORM comparison

Compare within a runtime. Cross-runtime numbers mix in the JIT, the GC and the ORM and are indicative only.

| Runtime | Baseline arm | Foundgine arm | Reference arm |
|---|---|---|---|
| C# | Hot Chocolate + EF Core (`hotchocolate`, :4300) | Foundgine GraphQL + Foundgine EF Core provider (`foundgine-ef`, :4303) | Foundgine native SQL (`foundgine`, :4302) |
| Java | Spring GraphQL + Hibernate/JPA | Foundgine GraphQL + Foundgine JPA provider | (step 2, not started) |

NHibernate is deliberately not an arm: .NET only, no Hot Chocolate integration (hand-written resolvers would decide the result), no Foundgine provider. Add it only as an optional fifth .NET baseline if asked.

## What holds constant in the C# pair
- Same `BankingEntityContext`, schema, seed data, EF Core 10.0.14 and Npgsql 10.0.3 (`tests/test_orm_pairs.py` checks the versions match).
- Same registration, `AddDbContext` (not pooled) in both arms.
- Same semantic model, metadata and result shaper for both Foundgine arms (linked files, not copies).
- Equal CPU/memory limits and cpusets in `compose/perf.override.yml`.
- Same scenarios (`scenarios/cb-*.json`); the runner's response-equivalence probe rejects a cell whose JSON differs.

## Known differences that are part of the measurement
- Foundgine-EF mutations go through the ORM unit of work, not the native arm's single `unnest()` statement.
- Hot Chocolate's upsert uses hand-written `INSERT ... ON CONFLICT`; the Foundgine-EF upsert is the provider's own. Both are the code each team would ship, but they are not the same SQL.
- Plan cache: `foundgine-ef` runs the `warm` endpoint (provider-plan cache on). The `cold` endpoint exists in the code but has no target yet.

## Status (step 1)
Written, **not compiled, not run** (no .NET SDK, Docker or Maven here). Verified: `python3 -m unittest discover -s tests` (43 tests, includes `test_orm_pairs.py` wiring checks).

Risks to check first when you build:
1. `ContractType` is `int?` in the hand-written metadata but an enum on the EF entity; the `FluentOrmMap` reads the CLR type, but the mutation path's int-to-enum conversion is unconfirmed.
2. `Process.ProcessedDateTime` is non-nullable; inserts through the ORM may need a value (the Hot Chocolate arm avoids this by using raw SQL for its upsert).
3. A cached `OrmPlan` is shared across requests in warm mode; confirm literal values are parameters, not baked in. The equivalence probe should catch it if not.
4. Not added to `Foundgine.sln` (hand-editing GUIDs blind is riskier than `dotnet sln add`).

Build and smoke test:
```
dotnet build src/csharp/benchmarks/CoffeeBeanery.Performance/Foundgine.EfCore.CoffeeBeanery.BenchmarkApi
cd src/csharp/benchmarks/CoffeeBeanery.Performance
docker compose -f compose/query.yml -f ../../../../tools/foundgine-perf/compose/perf.override.yml up -d --build postgres database foundgine-ef
curl -s localhost:4303/graphql/warm -H 'content-type: application/json' -d @../../../../tools/foundgine-perf/scenarios/cb-query-top50.json | head -c 300
```
(the scenario file wraps the body under `requests[0].body`; send that body.)
