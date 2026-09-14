# AI agent integration

Foundgine's AI integration is deliberately small: it exposes semantic execution as tools without making Foundgine depend on a particular LLM provider or agent framework.


## AI agents use the same canonical lifecycle

An agent is another untrusted caller. It does not receive a special execution path:

![PlantUML diagram: AI-AGENT, diagram 1](assets/ai-agent-plantuml-01.svg)

Capability discovery is descriptive. Search results, model output and tool arguments remain untrusted until the normal semantic and authorization stages accept them.

## Architecture

![PlantUML diagram: AI-AGENT, diagram 2](assets/ai-agent-plantuml-02.svg)

The model is an untrusted producer of intent.

## What `Foundgine.Providers.Models` provides

The package contains:

- `FoundgineAiToolset`;
- `FoundgineAiAgent`.

The toolset exposes semantic capability discovery and query execution as `AIFunction`s.

The agent helper runs a bounded function-calling loop using `Microsoft.Extensions.AI`.

## Capability discovery

An agent can first discover the semantic capabilities available to its execution context.

The flow is:

![PlantUML diagram: AI-AGENT, diagram 3](assets/ai-agent-plantuml-03.svg)

Discovery is descriptive.

The request is still authorized when executed:

![PlantUML diagram: AI-AGENT, diagram 4](assets/ai-agent-plantuml-04.svg)

## Security boundary

Never let model-generated tool arguments choose:

- identity;
- tenant;
- audience;
- warrant;
- provider;
- connection string;
- authorization policy.

The host supplies the trusted execution context.

## Example shape

```csharp
var toolset = new FoundgineAiToolset(
    foundgine,
    executionContextFactory);

var tools = toolset.CreateTools();
```

The application supplies its preferred `IChatClient` and can then use the returned tools in its agent loop.

## `FoundgineAiAgent`

The agent helper uses Microsoft.Extensions.AI function invocation to support:

![PlantUML diagram: AI-AGENT, diagram 5](assets/ai-agent-plantuml-05.svg)

The loop is bounded by tool-iteration/resource controls.

It is not a general autonomous-agent runtime.

## What remains application-owned

The host/application owns:

- model/provider credentials;
- model selection;
- system prompts;
- conversation memory;
- business approval;
- authentication;
- identity/tenant context;
- rate limiting;
- deployment;
- observability.

Foundgine only owns the semantic execution boundary.

## Prompt injection

Foundgine cannot make arbitrary natural-language instructions trustworthy.

The security strategy is instead to make the execution authority independent of the model:

![PlantUML diagram: AI-AGENT, diagram 6](assets/ai-agent-plantuml-06.svg)

Malicious text in data must not be able to grant the model new Foundgine authority.

Applications still need model/application-level prompt-injection defenses.

## Why not SQL generation?

The intended pattern is:

![PlantUML diagram: AI-AGENT, diagram 7](assets/ai-agent-plantuml-07.svg)

not:

![PlantUML diagram: AI-AGENT, diagram 8](assets/ai-agent-plantuml-08.svg)

This keeps database credentials and physical schema outside the model's control.

## Relationship to MCP

AI and MCP are independent adapters over the same runtime:

![PlantUML diagram: AI-AGENT, diagram 9](assets/ai-agent-plantuml-09.svg)

![PlantUML diagram: AI-AGENT, diagram 10](assets/ai-agent-plantuml-10.svg)

An application can use either or both.

## Relationship to GraphQL/JSON

GraphQL and JSON are also intent adapters.

The important property is convergence:

![PlantUML diagram: AI-AGENT, diagram 11](assets/ai-agent-plantuml-11.svg)

## Current scope

This integration does not claim to be:

- an agent framework;
- an LLM gateway;
- an autonomous workflow engine;
- a memory/vector system;
- a model safety platform.

It is a controlled AI tool integration.

## Related source package

See `src/csharp/Foundgine.Providers/Foundgine.Providers.Models/README.md` for the package-level API and security contract.

---

Next: [PostgreSQL E2E](POSTGRES-E2E.md)
