# Convention / AOT track: measuring "why Foundgine"

Claim to substantiate: *with Foundgine, the semantic model, metadata and execution maps are derived by
convention and generated at compile time (Roslyn source generator, Native AOT-safe), on top of real
frameworks (EF Core, ASP.NET, Temporal, Microsoft Agent Framework), instead of being hand-written per surface.*

## What the repo shows today (measured with `convention/footprint.py`, non-blank non-comment C# lines)
| Project | Hand-written LOC | Generator attributes | Manual registrations |
|---|--:|--:|--:|
| `Foundgine.CoffeeBeanery.BenchmarkApi` | 379 | 0 | 13 |
| `HotChocolate.CoffeeBeanery.BenchmarkApi` | 202 | 0 | 0 |
| `CoffeeBeanery.Database` (entities shared by both) | 1,511 | 0 | n/a |
| `aot/Foundgine.Aot.EfCore` (smoke app) | 82 | 1 entity / 3 fields | 0 |

**Consequence:** the benchmark Foundgine API builds `SemanticModel` and `MetadataRegistry` by hand
(`CoffeeBeanerySemanticModel.cs`, `CoffeeBeaneryMetadata.cs`). It does not exercise `Foundgine.Providers.Orm.Generator`.
Throughput numbers therefore say nothing yet about the convention path, and the line counts currently favour the baseline.
Do not publish "less scaffolding" until the arm below exists and was measured.

## Three-arm experiment (same Postgres fixture, same GraphQL/MCP contract, same limits)
| Arm | Model definition | Purpose |
|---|---|---|
| A. Hot Chocolate + EF Core | EF entities + HC types/resolvers | conventional baseline |
| B. Foundgine, hand-built (today) | `SemanticModelBuilder` + `MetadataRegistry` | isolates engine from convention |
| C. Foundgine, convention + generator (TO BUILD) | `[FoundgineEntity]/[FoundgineField]/[FoundgineRelationship]` on the EF entities; `GeneratedOrmMaps.CreateAll()` | the claim under test |

Arm C must derive from the same entity classes as arm A (no duplicate model). Equivalence check gates all arms.

## Metrics (each with N>=5 independent sessions and an interval)
1. **Authoring effort**: hand-written LOC and manual registrations per entity (`footprint.py`), generated LOC with
   `--generated` (build with `-p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=gen`).
   Add *time to first working endpoint* for a new entity (scripted: add entity -> build -> equivalent response), median of 10 runs.
2. **Change cost**: add a field/relationship to one entity; count files and lines touched per arm.
3. **Runtime**: closed/open-loop throughput and latency per arm (`fpbench`), CPU and RSS per request.
4. **AOT**: `dotnet publish -p:PublishAot=true` vs JIT for arm C: publish time, binary size, cold start to first 200
   and RSS (`convention/startup_meter.py`), plus the existing AOT warning baseline in `src/csharp/aot` as a gate.
5. **Convention coverage**: fields that rely on defaults vs `StorageName` overrides (reported by `footprint.py`).

## Claim wording rules
- Report LOC/effort as measured counts, not "x% faster development".
- AOT claims name the RID, .NET version and warning baseline status; cross-publish without native run is not evidence.
- Engine speed claims come from arm B vs A, convention claims from arm C vs B (effort) - never mixed.
