# Checkpoint 2: configuration volume (Foundgine side only)

Source: `src/csharp/samples/Foundgine.Mapping.Benchmark/Scenarios/*.cs`, counted by `measure.py`
(non-blank lines between `// BEGIN CONFIG` and `// END CONFIG`). Every scenario file is executed by the harness:
it must resolve its tenant scope to the expected table/column and survive a snapshot round-trip (wip8 also compares each against emitted C# metadata; not yet run). The line counts below are unchanged by wip8.
Domain classes (`Domain.cs`) are shared and excluded.

| Scenario | Config lines | Auth lines | Tables |
|---|---:|---:|---:|
| Simple | 5 | 1 | 1 |
| Renamed | 5 | 1 | 1 |
| Flattened | 6 | 1 | 1 |
| Owned (value object in its own table) | 6 | 1 | 2 |
| Complex (Order, Lines, Payments) | 8 | 2 | 3 |
| Inheritance TPH | 3 | 1 | 2 |
| Inheritance TPT | 3 | 1 | 2 |
| Many-to-many | 8 | 2 | 2 |
| Computed | 6 | 1 | 1 |
| Legacy schema | 8 | 1 | 1 |
| Multi-tenant shared table | 3 | 2 | 2 |
| **Total** | **61** | **14** | **18** |

## How to read this
- Authorization is 1-2 lines per aggregate (`Tenant`/`Owner`), declared once on the domain type. Child tables, join tables and
  TPT-derived tables are scoped through the mapping; no per-table rule is written.
- Counts include structural lines (`new MappingModel()`, braces) and depend on formatting. Treat as order-of-magnitude, not exact.
- "Config lines" is mapping configuration only. It excludes application/query code, which this harness does not have.

## What this is NOT
- No Hot Chocolate + EF Core measurement exists: the EF/HC side was never written or built (NuGet unreachable here), so there is
  no comparison number. Do not read the table above as a result against any competitor.
- No execution against a database; correctness is metadata + scope-resolution level only.

---
# Update (wip7): EF Core comparator, side by side

Run `python3 src/csharp/samples/Foundgine.Mapping.Benchmark/measure_compare.py`.

| Scenario | Foundgine | EF Core total (entities + config + projection) | Foundgine auth decls | EF query filters |
|---|---:|---:|---:|---:|
| Simple | 5 | 6 | 1 | 1 |
| Renamed | 5 | 6 | 1 | 1 |
| Flattened | 6 | 6 | 1 | 1 |
| Owned | 6 | 12 | 1 | 1 |
| Complex | 8 | 19 | 2 | 3 |
| Inheritance TPH | 3 | 9 | 1 | 1 |
| Inheritance TPT | 3 | 10 | 1 | 1 |
| Many-to-many | 8 | 16 | 2 | 2 |
| Computed | 6 | 7 | 1 | 1 |
| Legacy | 8 | 16 | 1 | 1 |
| Multi-tenant shared table | 3 | 8 | 2 | 1 |
| **Total (lines)** | **61** | **115** | **14** | **14** |

Formatting-independent: **2,248** vs **8,420** non-whitespace characters (3.75x).

## How much weight this can bear: not much yet
- The two measures disagree (1.9x by lines, 3.75x by characters) because the EF draft uses long single lines. Neither is a precise number.
- **Same author, unbuilt comparator.** I wrote both sides, and the EF side has never been compiled. It could be both too short (missing
  fixes needed to make it run) and too long (an EF expert might write it tighter). Independent authorship and a build are needed before
  quoting any ratio externally.
- For simple conventional cases (Simple, Renamed, Flattened, Computed) the two are within about 1-2 lines. The gap opens in the
  complex mappings: owned, aggregate, inheritance, many-to-many, legacy.
- Authorization: the *count* of declarations is equal (14 vs 14). The difference is where the logic lives: Foundgine states tenant/owner once per
  aggregate and resolves child/join/derived tables through the mapping; EF repeats the full boolean expression on every entity (Complex: three
  filters for one aggregate). That is a maintenance and correctness-risk argument, not a line-count one.
- Not compared: runtime performance, query correctness against a database, or authorization behaviour under EF's filter edge cases
  (for example, how a many-to-many join row is filtered). The draft's many-to-many filters the two ends only; behaviour of the join table is unverified.
