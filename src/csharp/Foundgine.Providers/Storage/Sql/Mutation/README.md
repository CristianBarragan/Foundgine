# SQL mutations

Compiles provider-neutral mutation plans into parameterized SQL.

The provider owns SQL details such as `INSERT`, `UPDATE`, `DELETE`, conflict handling, and generated identity retrieval.

## PostgreSQL 17 correlation execution — PostgreSQL 17 correlation execution

Batched Create uses PostgreSQL 17 `MERGE ... RETURNING` so the compiler-owned `__fg_corr` source ordinal can be returned alongside generated target values. This removes the need for a user-visible natural key or a post-insert target-table lookup for Create correlation.

The integration proof executes the generated top-level CTE statement against PostgreSQL 17 and reverses only the terminal result ordering. Logical operation mapping remains keyed by `__fg_corr`.
