<picture>
  <source media="(prefers-color-scheme: dark)" src="docs-site/assets/logo/foundgine-logo.svg">
  <img src="docs-site/assets/logo/foundgine-logo.svg" alt="Foundgine" width="360">
</picture>

[![NuGet Version](https://img.shields.io/nuget/v/Foundgine.Core?label=NuGet%20Version)](https://www.nuget.org/packages/Foundgine.Core/)
[![Unit Tests](https://img.shields.io/github/actions/workflow/status/Foundgine/Foundgine/csharp-build.yml?branch=main&job=unit-tests&label=Unit%20Tests)](https://github.com/Foundgine/Foundgine/actions/workflows/csharp-build.yml)
[![Integration Tests](https://img.shields.io/github/actions/workflow/status/Foundgine/Foundgine/csharp-build.yml?branch=main&job=integration-tests&label=Integration%20Tests)](https://github.com/Foundgine/Foundgine/actions/workflows/csharp-build.yml)
[![Maven Central Version](https://img.shields.io/maven-central/v/io.github.cristianbarragan/foundgine-core?label=Maven%20Central)](https://central.sonatype.com/artifact/io.github.cristianbarragan/foundgine-core)
[![Java Unit Tests](https://img.shields.io/github/actions/workflow/status/Foundgine/Foundgine/java-build.yml?branch=main&job=java-build&label=Java%20Unit%20Tests)](https://github.com/Foundgine/Foundgine/actions/workflows/java-build.yml)
[![Python Version](https://img.shields.io/pypi/v/foundgine?label=PyPI%20Version)](https://pypi.org/project/foundgine)
[![TypeScript Version](https://img.shields.io/npm/v/@foundgine/typescript?label=npm)](https://www.npmjs.com/package/@foundgine/typescript)
[![WASM Version](https://img.shields.io/npm/v/@foundgine/wasm?label=npm%20WASM)](https://www.npmjs.com/package/@foundgine/wasm)

# Foundgine

**A configurable semantic and execution boundary for application capabilities.**

Foundgine separates application meaning, persistence mapping, authorization and execution from the interfaces and agents that consume them. Configure capabilities with fluent APIs, resolve operations against application-defined semantics, and execute through providers—without making a particular API, ORM, database, or AI framework the center of your architecture.

It is built for conventional application development as well as applications that expose capabilities to AI agents. **AI is an optional consumer, not a prerequisite.**

## Why Foundgine?

Applications commonly duplicate business meaning, query logic, validation and authorization across REST endpoints, GraphQL resolvers, internal services, automation and agent tools. That duplication makes behavior harder to keep consistent and increases the cost of adding another interface.

Foundgine provides a configurable boundary where applications define capabilities and their semantics, map them to their persistence and domain environment, and control execution. Interfaces consume that boundary instead of independently implementing its rules.

- **Configure meaning, not transport:** define entities, fields, relationships, aliases and capabilities through fluent configuration.
- **Separate concerns:** semantic configuration, persistence mapping, authorization and execution are distinct responsibilities.
- **Use familiar infrastructure:** integrate with existing EF Core, JPA, databases and application architecture; Foundgine is not a mandate to replace them.
- **Reuse across consumers:** application code, supported APIs, GraphQL, MCP and agent integrations can use the same configured capabilities.
- **Adopt incrementally:** start with the operations where a shared execution boundary is useful. The semantic/AI features are optional.

## Architecture at a glance

```text
Application / API / GraphQL / MCP / Agent / Other Consumer
                         │
                         ▼
          Configured Semantic Capabilities
                         │
          Meaning · Mapping · Relationships
                         │
                         ▼
             Resolution and Authorization
                         │
                         ▼
              Plan / Execution Boundary
                         │
                         ▼
        Provider / ORM / Existing Infrastructure
```

The application remains responsible for its trusted identity and security context, domain invariants, and policies. Foundgine does not infer authority from caller text, retrieval results, model output or capability discovery. Authorization is applied at the execution boundary.

## Fluent configuration is the primary experience

Foundgine's configuration model is designed to be explicit and language-idiomatic in both C# and Java. The semantic model and persistence mapping are configured separately from the domain types and from transport-specific endpoints. Consult the language samples for the exact APIs supported by the current release.

- [.NET starter](src/csharp/samples/Foundgine.SupplyChain/)
- [Java starter](src/java/samples/foundgine-supply-chain/)
- [Advanced .NET sample](src/csharp/samples/Foundgine.SupplyChain.Advanced/docs/00-Overview-And-Setup.md)
- [Advanced Java sample](src/java/samples/foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md)

## Use Foundgine without AI

Foundgine can be used for semantic query and execution, relationships, provider-backed operations and application interfaces without an LLM, MCP or agent framework. AI integrations are optional adapters that consume capabilities; they do not define application authority.

## Existing stack and incremental adoption

Foundgine complements rather than universally replaces:

- **EF Core and JPA:** ORM integration and provider paths are available; capability maturity and constraints are documented per provider.
- **GraphQL:** adapters are available for Hot Chocolate (.NET) and GraphQL Java.
- **Databases and providers:** use supported provider paths and existing infrastructure; see the provider catalogue.
- **Identity and authorization:** host/application supplies trusted execution context and policy. Foundgine is not an identity provider or authorization server.
- **Workflow and agent frameworks:** optional integrations, including Temporal and agent-framework adapters, are documented separately.

Adopt it around a selected capability, validate behavior and operational fit, then expand as appropriate. Do not assume every existing application or operation needs to move behind Foundgine.

## Examples and comparisons

- **Starter applications:** [.NET](src/csharp/samples/Foundgine.SupplyChain/) · [Java](src/java/samples/foundgine-supply-chain/)
- **Advanced semantic and security examples:** [.NET](src/csharp/samples/Foundgine.SupplyChain.Advanced/docs/00-Overview-And-Setup.md) · [Java](src/java/samples/foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md)
- **Conventional stack comparisons:** [.NET — Hot Chocolate and EF Core](src/csharp/samples/Foundgine.Comparison/README.md) · [Java — Spring](src/java/samples/foundgine-comparison/README.md)
- [Alias grounding example](docs/ALIAS-EXAMPLE.md) · [Grounding decisions](docs/GROUNDING-DECISIONS.md)

Comparisons are workload-specific. Foundgine does not claim to replace every ORM, API framework or provider, nor that it is universally faster or inherently more secure than every alternative.

## Documentation

| Goal | Resource |
|---|---|
| Product overview | [What is Foundgine?](https://foundgine.github.io/Foundgine/docs-site/what-is-foundgine.html) |
| Get running | [Getting started](docs/GETTING-STARTED.md) |
| Understand boundaries | [Architecture](docs/ARCHITECTURE.md) · [Authorization](docs/AUTHORIZATION.md) · [Security](docs/SECURITY.md) |
| Browse concepts and features | [Wiki](docs/wiki/index.md) |
| Choose persistence and providers | [Provider catalogue](docs/wiki/providers.md) · [ORM providers](docs/wiki/orm-providers.md) |
| Configure extensions | [Extensions](docs/wiki/extensions.md) |
| Integrate agents and MCP | [AI agents and MCP](docs/wiki/agents-mcp.md) · [Framework integrations](docs/wiki/agent-framework-integrations.md) |
| Client SDKs | [TypeScript, Python and WebAssembly](docs/wiki/client-sdks.md) |
| Packages | [Package guide](docs-site/packages/) |
| Testing and evidence | [Testing](docs/TESTING.md) · [Benchmarks](https://foundgine.github.io/Foundgine/docs-site/agent-benchmark/) · [PenTest](https://foundgine.github.io/Foundgine/docs-site/samples/pentest/) · [Documentation audit](docs/DOCUMENTATION-AUDIT.md) |
| Website | [Foundgine documentation site](https://foundgine.github.io/Foundgine/docs-site/) |

## Development

```bash
cd src/csharp && dotnet build Foundgine.sln && dotnet test Foundgine.sln
cd src/java && mvn -U clean verify -Dgpg.skip=true
```

See [testing](docs/TESTING.md), [PostgreSQL E2E](docs/POSTGRES-E2E.md) and [changelog](CHANGELOG.md) for prerequisites and verification details. Version and verification status can differ by package, language and integration; consult release artifacts and the relevant documentation.

## AI and repository guidance

- [`llms.txt`](llms.txt): concise discovery and canonical links
- [`llms-full.md`](llms-full.md): extended technical context and capability status
- [`AGENTS.md`](AGENTS.md): coding-agent guidance for this repository
- [`docs/AI-AGENT.md`](docs/AI-AGENT.md): runtime AI integration and security boundary

## License

Apache License 2.0.
