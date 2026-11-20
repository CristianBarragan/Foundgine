# Why Foundgine

Foundgine is a configurable semantic and execution boundary for application capabilities. The comparison is with conventional application stacks as well as agent-oriented tooling. It is not a claim that Foundgine replaces every API framework, ORM or database. Fluent configuration is the primary experience; AI and MCP are optional consumers. Evaluate it against a representative workload and your existing architecture, including operational complexity and migration cost.
: the architectural argument

## The problem with assembling access behavior one interface at a time

An ORM maps objects to storage. An API framework exposes operations. An authorization framework evaluates identity and policy. An agent integration exposes tools. These are useful building blocks, but an application can end up expressing the same entity relationships, filters, access rules and execution constraints in several places.

That duplication is manageable in a small API. It becomes harder to reason about as the application adds protocols, nested queries, automation and AI callers.

## Foundgine's proposition

Foundgine provides a configurable semantic and execution boundary between application capabilities and the callers that exercise them.

Its configuration separates:

1. **Semantic model:** what application entities and relationships mean.
2. **Persistence mapping:** where those entities and fields live in existing storage.
3. **Connections:** how relationships are physically joined.
4. **Authorization:** which operations and data a caller may exercise.
5. **Intent and execution:** what the caller requests and how an authorized operation is planned and executed.

The central distinction is: metadata describes what exists; semantic configuration describes what it means; authorization describes what may be exercised; intent describes what the caller wants.

## Why fluent configuration

Fluent configuration makes the model explicit, composable and reviewable in application code. It can describe an existing database without requiring the database schema to become the public API model. It also avoids making the semantic model depend on one transport's resolver conventions.

The C# and Java comparison samples should present this as the primary Foundgine experience. Java's current JPA sample still uses `@FoundgineOrmEntity` as an AOT discovery marker; this is separate from semantic configuration and must not be described as a fully annotation-free registration path.

## What Foundgine can complement or replace

- **Can complement:** EF Core, Hibernate/JPA, GraphQL frameworks, REST endpoints and existing databases.
- **Can centralize:** model relationships, model-driven query behavior, mapping, authorization-aware planning and execution controls.
- **Can expose through:** supported API and agent integrations, provided each integration is actually wired and tested against the same model.
- **Does not automatically replace:** an ORM, database, identity provider, API framework or workflow engine.

## The adoption path

### 1. Conventional API, no semantic intent layer

Start by configuring the model and persistence mapping, then use Foundgine with a conventional API. No AI or natural-language capability is required.

### 2. Shared model and policy across interfaces

Add another interface, such as MCP, against the same configured model and authorization boundary. Avoid implementing a second set of data access rules for that interface.

### 3. Semantic discovery and intent execution

Introduce semantic retrieval and intent resolution when useful. Discovery does not grant execution authority: authorization and execution controls remain separate gates.

## Claims the comparison samples should make

- Foundgine centralizes model and relationship declarations in fluent configuration.
- Foundgine can apply authorization within its execution path, including nested entity traversal where the provider supports it.
- Foundgine can integrate with existing persistence technologies rather than requiring a database replacement.
- The same model can be reused by multiple protocols when those protocol adapters are configured and tested.

## Claims the samples should not make without evidence

- That Foundgine always produces less application code. The current C# and Java samples contain more sample code than their baselines.
- That Foundgine is universally faster.
- That every provider supports every query shape identically.
- That the Java comparison is annotation-free. It is not yet: JPA annotations remain, and `@FoundgineOrmEntity` currently drives AOT persistence discovery.
- That MCP or another protocol is demonstrated by a comparison unless the sample actually wires it to the same model and includes parity coverage.

## Short positioning statement

**Foundgine is a configurable semantic and execution boundary for application capabilities. It separates what an application can do, what a caller is authorized to do, and how an operation is executed—so APIs and agents can share one governed model instead of rebuilding data access rules for every interface.**
