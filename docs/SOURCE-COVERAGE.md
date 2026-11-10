# Source coverage and implementation inventory

This page is generated/checked against the supplied `src-fixed.zip` source tree. It is an inventory of implementation surfaces, not a claim that every feature is production-certified. For runtime and security guarantees, consult the feature page and its tests/conformance evidence.

## Build targets and version evidence

| Runtime | Value found in source | Evidence |
|---|---|---|
| Foundgine release line | `2.3.1` | `src/java/pom.xml` `<revision>` |
| .NET | `net10.0` | C# project files, including `Foundgine.Core.csproj` |
| Java | `27` | `src/java/pom.xml` `maven.compiler.release` |

The Java requirement is **Java 27**, not Java 21. Do not build or publish the Java modules using a Java 21 toolchain and treat that as a valid verification of this source snapshot.

## Product modules

### .NET projects

| Project | Responsibility |
|---|---|
| `Foundgine.Core` | Provider-independent contracts, semantic model, metadata, intent, resolution, planning, execution IR and evidence |
| `Foundgine.Runtime` | Application orchestration, query/mutation APIs, execution coordination, hosting, routing and optional control plane |
| `Foundgine.Providers` | SQL, InMemory, Elasticsearch, PostgreSQL vector retrieval, MCP, AI/model tools, gateway, event store, outbox and AOT metadata |
| `Foundgine.Providers.Orm` | ORM-neutral execution layer over typed `IQueryable` expression fragments |
| `Foundgine.Providers.Orm.Generator` | Build-time generation of ORM maps |
| `Foundgine.Providers.EntityFrameworkCore` | EF Core execution adapter |
| `Foundgine.Extensions` | GraphQL/Hot Chocolate, Keycloak and Temporal integrations |

Additional code-bearing areas include `tests`, `samples`, `benchmarks`, `security`, `conformance`, and `aot`. They are not runtime library packages, but they are part of the verification and adoption surface and must remain represented in the docs.

### Java Maven modules

| Module | Responsibility |
|---|---|
| `foundgine-core` | Semantic contracts, metadata, intent, resolution, planning and execution contracts |
| `foundgine-runtime` | Runtime orchestration, APIs, authorization, mutation lifecycle and control-plane facilities |
| `foundgine-providers` | SQL/JDBC, InMemory, ORM/JPA, Elasticsearch, PostgreSQL vector retrieval, MCP, model tools, gateway, event store, outbox and AOT support |
| `foundgine-extensions` | GraphQL Java, Keycloak and Temporal integrations |
| `foundgine-aot-generator` | Separate annotation-processor module directory; **not listed in the root Maven reactor** in this source snapshot |
| `security/foundgine-redteam` | Security/red-team module, included in the Maven reactor but excluded from public artifact publishing |
| `samples/*` | Starter and advanced Supply Chain sample applications |

The Maven parent declares the four public libraries separately from internal sample and security artifacts. Do not describe every reactor module as a published library.

## Provider and integration coverage verified in source

| Capability | .NET source | Java source |
|---|---|---|
| SQL execution | `Foundgine.Providers/Storage/Sql` | `foundgine-providers/.../storage/sql` |
| In-memory execution | `Foundgine.Providers/Storage/InMemory` | `foundgine-providers/.../storage/inmemory` |
| Elasticsearch lexical retrieval | `ElasticsearchSemanticLexicalCandidateSource`, `SemanticLexiconIndexClient` | `ElasticsearchSemanticLexicalCandidateSource`, `SemanticLexiconIndexClient` |
| PostgreSQL pgvector lexical retrieval | `PgVectorSemanticLexicalCandidateSource`, `PgVectorSemanticLexiconIndexClient` | `PgVectorSemanticLexicalCandidateSource`, `PgVectorSemanticLexiconIndexClient` |
| ORM-neutral execution | `Foundgine.Providers.Orm` | `storage/orm` |
| Concrete ORM adapter | EF Core: `Foundgine.Providers.EntityFrameworkCore` | JPA: `storage/orm/jpa` |
| ORM map generation | `Foundgine.Providers.Orm.Generator` | No corresponding dedicated ORM map generator module identified in this source tree |
| MCP | `Foundgine.Providers/Tools/MCP` | `providers/tools/mcp` |
| Gateway / events / outbox | `Foundgine.Providers/Gateway`, `EventStore`, `Outbox` | `providers/gateway`, `eventstore`, `outbox` |
| Keycloak | `Foundgine.Extensions/Keycloak` | `extensions/keycloak` |
| Temporal | `Foundgine.Extensions/Temporal` | `extensions/temporal` |
| GraphQL | Hot Chocolate adapter | GraphQL Java adapter |

Presence in both source trees does not prove identical behavior, supported feature sets, or production certification. Use the ORM conformance matrix and language-specific tests for those claims.

## Important corrections to older documentation

- PostgreSQL vector retrieval **is present** in both code trees (`PostgresVector` / `postgresvector`); older text saying vector retrieval is not implemented is stale.
- Elasticsearch semantic lexical candidate/index components are present in both code trees.
- GraphQL adapters are present in both code trees, but they are different framework integrations: Hot Chocolate for .NET and GraphQL Java for Java. Do not call them framework-identical.
- The Java Maven compiler release is 27 in the supplied source snapshot.
- ORM support is not one undifferentiated feature: the .NET tree has a shared ORM core, EF Core adapter and map generator; Java has a shared ORM core and JPA adapter. The level of runtime/database/AOT verification differs by capability.

## Tests, samples and verification surfaces

The source tree contains dedicated .NET test projects for semantic behavior, planning, SQL, InMemory, ORM, MCP, GraphQL, AOT, event store, gateway, security/authority, high assurance and end-to-end behavior. It also contains security harnesses, conformance data and runnable Supply Chain / Temporal samples.

Java verification is distributed across module tests, the `foundgine-redteam` module, ORM/JPA parity plan and the starter/advanced Supply Chain samples. The separate `foundgine-aot-generator` directory is not a root reactor module in the supplied parent POM; processor sources also exist in `foundgine-providers`. A source file or test class is not evidence that a test has passed. Record executed build/test commands and environment in the relevant conformance page.

## Completeness rule for future changes

When a project, public namespace, provider family, extension, generator, sample or test project is added or removed, update all of the following in the same change:

1. This inventory and the cross-language implementation map.
2. The relevant feature page and package README.
3. `CURRENT-STATUS.md` if capability or verification status changes.
4. `mkdocs.yml`, `llms.txt`, and `llms-full.txt` when the public documentation surface changes.
5. Examples and diagrams whose call paths or boundaries changed.

## Complete project-file inventory (source snapshot)

The lists below were enumerated from the supplied source archive. They make it possible to spot a project added to code without a corresponding documentation update.

### .NET project files (`.csproj`)

- `csharp/Foundgine.Core/Foundgine.Core.csproj`
- `csharp/Foundgine.Extensions/Foundgine.Extensions.csproj`
- `csharp/Foundgine.Extensions/Samples/Temporal.Api/Temporal.Api.csproj`
- `csharp/Foundgine.Extensions/Samples/Temporal.Worker/Temporal.Worker.csproj`
- `csharp/Foundgine.Providers.EntityFrameworkCore/Foundgine.Providers.EntityFrameworkCore.csproj`
- `csharp/Foundgine.Providers.Orm.Generator/Foundgine.Providers.Orm.Generator.csproj`
- `csharp/Foundgine.Providers.Orm/Foundgine.Providers.Orm.csproj`
- `csharp/Foundgine.Providers/EventStore/Foundgine.Providers.EventStore.csproj`
- `csharp/Foundgine.Providers/Foundgine.Providers.Aot.Generator/Foundgine.Providers.Aot.Generator.csproj`
- `csharp/Foundgine.Providers/Foundgine.Providers.csproj`
- `csharp/Foundgine.Providers/Samples/Gateway.MinimalApi/Gateway.MinimalApi.csproj`
- `csharp/Foundgine.Runtime/Foundgine.Runtime.csproj`
- `csharp/aot/Foundgine.Aot.EfCore/Foundgine.Aot.EfCore.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/HighAssurance.Banking/Foundgine.HighAssurance.Banking.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/HighAssurance.Postgres/Foundgine.HighAssurance.Postgres.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered/Api/Foundgine.SupplyChain.Api.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered/Application/Foundgine.SupplyChain.Application.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered/Domain/Foundgine.SupplyChain.Domain.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered/Infrastructure/Foundgine.SupplyChain.Infrastructure.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered/Tests/Foundgine.SupplyChain.Tests.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.PenTest/Api/GraphQL/Foundgine.SupplyChain.PenTest.Graph.Api.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.PenTest/Api/Mcp/Foundgine.SupplyChain.PenTest.Mcp.Api.csproj`
- `csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.PenTest/Tests/Foundgine.SupplyChain.PenTest.Tests.csproj`
- `csharp/benchmarks/AgentEndToEnd/Foundgine.AgentEndToEnd.Benchmark.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run1/Foundgine.AgentEndToEnd.Run1.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run2/Foundgine.AgentEndToEnd.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run3/Foundgine.AgentEndToEnd.Run3.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run4/GraphQL.EF/GraphQL.EF.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run4/MCP.Foundgine/MCP.Foundgine.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run4/Runner/Runner.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5/Database/Database.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5/EfCore/Foundgine.HighAssurance.EfCore.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5/MCP.EfCore/MCP.EfCore.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5/MCP.Foundgine/MCP.Foundgine.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5/Runner/Runner.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5SameClient/Database/Database.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5SameClient/EfCore/Foundgine.HighAssurance.EfCore.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5SameClient/MCP.EfCore/MCP.EfCore.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5SameClient/MCP.Foundgine/MCP.Foundgine.csproj`
- `csharp/benchmarks/AgentEndToEnd/Run5SameClient/Runner/Runner.csproj`
- `csharp/benchmarks/CoffeeBeanery.Performance/CoffeeBeanery.Database/CoffeeBeanery.Database.csproj`
- `csharp/benchmarks/CoffeeBeanery.Performance/CoffeeBeanery.LoadTest/CoffeeBeanery.LoadTest.csproj`
- `csharp/benchmarks/CoffeeBeanery.Performance/Foundgine.CoffeeBeanery.BenchmarkApi/Foundgine.CoffeeBeanery.BenchmarkApi.csproj`
- `csharp/benchmarks/CoffeeBeanery.Performance/HotChocolate.CoffeeBeanery.BenchmarkApi/HotChocolate.CoffeeBeanery.BenchmarkApi.csproj`
- `csharp/samples/Foundgine.SupplyChain.Advanced/Agent/Agent.csproj`
- `csharp/samples/Foundgine.SupplyChain.Advanced/Database/Database.csproj`
- `csharp/samples/Foundgine.SupplyChain.Advanced/MCP.Foundgine/MCP.Foundgine.csproj`
- `csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Api/Mcp/Foundgine.SupplyChain.Advanced.Mcp.Api.csproj`
- `csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Benchmarks/Foundgine.SupplyChain.Advanced.Benchmarks.csproj`
- `csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Foundgine.SupplyChain.Advanced.csproj`
- `csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/McpClient/Foundgine.SupplyChain.Advanced.Mcp.Client.csproj`
- `csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Tests/Foundgine.SupplyChain.Advanced.Tests.csproj`
- `csharp/samples/Foundgine.SupplyChain/Foundgine.SupplyChain.csproj`
- `csharp/security/Foundgine.RedTeam/Foundgine.RedTeam.Security.csproj`
- `csharp/tests/Foundgine.Aot.Tests/Foundgine.Aot.Tests.csproj`
- `csharp/tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj`
- `csharp/tests/Foundgine.EventStore.Tests/Foundgine.EventStore.Tests.csproj`
- `csharp/tests/Foundgine.Gateway.Tests/Foundgine.Gateway.Tests.csproj`
- `csharp/tests/Foundgine.GraphQL.HotChocolate.Tests/Foundgine.GraphQL.HotChocolate.Tests.csproj`
- `csharp/tests/Foundgine.HighAssurance.Tests/Foundgine.HighAssurance.Tests.csproj`
- `csharp/tests/Foundgine.InMemory.Tests/Foundgine.InMemory.Tests.csproj`
- `csharp/tests/Foundgine.Intent.Json.Tests/Foundgine.Intent.Json.Tests.csproj`
- `csharp/tests/Foundgine.MCP.Tests/Foundgine.MCP.Tests.csproj`
- `csharp/tests/Foundgine.Orm.Tests/Foundgine.Orm.Tests.csproj`
- `csharp/tests/Foundgine.Planning.Tests/Foundgine.Planning.Tests.csproj`
- `csharp/tests/Foundgine.Postgres.Vector.Tests/Foundgine.Postgres.Vector.Tests.csproj`
- `csharp/tests/Foundgine.Security.Authority.Tests/Foundgine.Security.Authority.Tests.csproj`
- `csharp/tests/Foundgine.Security.Tests/Foundgine.Security.Tests.csproj`
- `csharp/tests/Foundgine.Semantics.Tests/Foundgine.Semantics.Tests.csproj`
- `csharp/tests/Foundgine.Sql.Tests/Foundgine.Sql.Tests.csproj`

### Java Maven project files (`pom.xml`)

- `java/benchmarks/foundgine-agent/pom.xml`
- `java/foundgine-aot-generator/pom.xml`
- `java/foundgine-core/pom.xml`
- `java/foundgine-extensions/pom.xml`
- `java/foundgine-providers/pom.xml`
- `java/foundgine-runtime/pom.xml`
- `java/pom.xml`
- `java/samples/foundgine-supply-chain-advanced/pom.xml`
- `java/samples/foundgine-supply-chain/pom.xml`
- `java/security/foundgine-redteam/pom.xml`

### Inventory totals

- .NET project files: **70**.
- Java Maven project files: **10** (including the standalone `foundgine-aot-generator` POM).
- C# production source files in the six principal library projects: **449**.
- Java production source files in the four principal library modules: **788**.
