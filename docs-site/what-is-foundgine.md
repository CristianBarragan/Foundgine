# What Is Foundgine?

Foundgine is a **configurable semantic and execution boundary for application capabilities**. It separates application meaning, persistence mapping, authorization and execution from the interfaces and agents that consume them.

## Built for application development—not only AI

Foundgine's primary developer experience is fluent configuration in C# and Java. Applications can use configured semantic capabilities from conventional application code and supported interfaces without requiring an LLM, MCP or agent framework.

## The problem

As applications expose the same capabilities through internal code, APIs, GraphQL, automation and agent tools, business meaning, persistence mapping, validation and authorization can become duplicated across entry points. That duplication makes changes harder to coordinate and behavior harder to verify.

## The boundary

Applications configure their semantic capabilities and persistence mapping. Foundgine resolves operations against that configuration, applies authorization using trusted application context, and executes through configured providers. Interfaces consume the same capability boundary rather than becoming independent sources of domain authority.

## Incremental adoption

Foundgine complements existing domain models, databases, EF Core, JPA and API frameworks. It does not require replacing the existing stack. Start with a capability, validate the implementation and operational fit, and expand where useful.

## Optional integrations

GraphQL, MCP, AI agents, workflow engines and client SDKs are additional consumers or integrations. They are not prerequisites for using Foundgine.

## What it is not

Foundgine is not an ORM replacement, database, identity provider, authorization server or general autonomous-agent framework. It makes no universal claim of being faster or inherently more secure than every conventional alternative; those outcomes depend on workload, configuration and verification.

## Continue

[Get started](getting-started/index.html) → [Architecture](architecture/index.html)
