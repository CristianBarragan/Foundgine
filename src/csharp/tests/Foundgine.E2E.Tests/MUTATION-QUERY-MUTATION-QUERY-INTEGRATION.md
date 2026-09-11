# PostgreSQL E2E — Mutation → Query → Mutation → Query

This is the stateful integration proof for the flagship scenario.

```text
Mutation #1
  semantic graph → execution IR → PostgreSQL batch SQL → PostgreSQL 17
       ↓
Query #1
  semantic request → execution IR → SQL → PostgreSQL 17
       ↓
Mutation #2
  semantic update → execution IR → PostgreSQL batch SQL → PostgreSQL 17
       ↓
Query #2
  same semantic query → SQL → PostgreSQL 17
```

All four operations share the **same PostgreSQL transaction**. The test proves:

1. Mutation #1 creates a generated Customer/Account graph.
2. Query #1 observes that newly-created graph through a nested relationship.
3. Mutation #2 changes the generated Account from `Open` to `Blocked` through Foundgine semantics, not raw SQL.
4. Query #2 observes the changed state and excludes the generated Customer because its only Account is now blocked.
5. Existing baseline customers remain visible, proving the second mutation changed the intended graph rather than invalidating the whole query.
6. The outer transaction is rolled back, so the database is left unchanged.

This test complements the separate PostgreSQL E2E measurement tests. It is intentionally a **stateful correctness/integration gate**; the EXPLAIN matrix remains responsible for physical-plan measurements.
