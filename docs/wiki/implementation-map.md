# Cross-language implementation map

This page maps capabilities to the repository's current module layout. It is an orientation guide, not a guarantee that every framework adapter or provider has identical feature coverage in C# and Java.

| Capability | C# location | Java location |
|---|---|---|
| Semantic contracts, types, identities | `src/csharp/Foundgine.Core/Semantic`, `Interfaces` | `src/java/foundgine-core/.../com/foundgine/core/semantic` |
| Model configuration and mapping | `Foundgine.Core/Semantic/Configuration` | `foundgine-core` semantic configuration packages |
| Intent and lexical grounding | `Foundgine.Core/Semantic/Intent`, `Resolution` | `foundgine-core` intent/resolution packages |
| Query and mutation planning | `Foundgine.Core/Semantic/Query`, `Mutation`, `Aggregates` | `foundgine-core` query/mutation/planning packages |
| Runtime orchestration and APIs | `src/csharp/Foundgine.Runtime` | `src/java/foundgine-runtime` |
| SQL, in-memory and retrieval providers | `src/csharp/Foundgine.Providers/Storage` | `src/java/foundgine-providers` |
| Gateway, event and outbox | `Foundgine.Providers/Gateway`, `EventStore`, `Outbox` | `foundgine-providers` gateway/event/outbox packages |
| MCP and agent tooling | `Foundgine.Providers/Tools/MCP`, runtime control plane | `foundgine-providers` tool/MCP packages and `foundgine-runtime` governance |
| AOT metadata | `Foundgine.Providers/Aot`, `Foundgine.Providers.Aot.Generator` | `foundgine-aot-generator` and provider annotations/metadata |
| Keycloak | `src/csharp/Foundgine.Extensions/Keycloak` | `src/java/foundgine-extensions/.../keycloak` |
| Temporal | `src/csharp/Foundgine.Extensions/Temporal` | `src/java/foundgine-extensions/.../temporal` |
| GraphQL adapter | `src/csharp/Foundgine.Extensions/GraphQL/HotChocolate` | Check `foundgine-extensions` for implemented adapter modules; do not assume Hot Chocolate parity |
| Samples and tests | `src/csharp/samples`, `tests`, `security` | `src/java/samples`, module tests, `security` |

## Module responsibilities

### Core

Owns provider-independent semantic vocabulary, identities, validation, resolution/planning contracts, and security primitives. Core should not depend on application-specific generated classes, a concrete database, or a transport framework.

### Runtime

Coordinates semantic requests, authorization, plans, execution lifecycle, control-plane governance, and hosting abstractions. Runtime should not become a second application service layer that embeds vendor-specific storage behavior.

### Providers

Implements physical execution and infrastructure adapters. Provider capabilities must be declared and validated; provider-specific semantics must not leak into core contracts.

### Extensions

Adapts third-party frameworks and services. Extensions should translate to/from Foundgine contracts and delegate the actual semantic and security decisions to the appropriate core/runtime services.

## How to verify a component

Search for the public interface/record/class in the relevant module, inspect its registration extension, and follow the call path into the implementation. Then locate tests that cover both success and denial/failure behavior. The wiki intentionally avoids reproducing every method signature because those change with the source and should be read from the current code or generated API documentation.
