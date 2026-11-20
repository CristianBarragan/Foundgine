# Why Foundgine?

Foundgine provides a configurable semantic and execution boundary for application capabilities. It is intended to solve duplication across conventional application interfaces as well as AI consumers—not to compete only with AI frameworks. Fluent configuration is the primary developer experience in C# and Java. Existing persistence, domain models and interface frameworks can remain; adopt the boundary incrementally where it adds value.


> **For technology leaders:** Foundgine is the semantic execution layer for enterprise applications and AI. It helps teams expose business capabilities through multiple interfaces while keeping business meaning, authorization and execution under application control.

## The problem it addresses

Enterprise systems rarely have just one way to access business data and operations. Employees use applications, services expose REST or GraphQL APIs, integrations connect bounded contexts, and AI agents introduce new callers. As these surfaces grow, teams can end up duplicating:

- business entity and relationship definitions;
- query, validation and mutation behavior;
- authorization, tenant and ownership checks;
- provider-specific execution logic; and
- audit and operational evidence.

That duplication increases maintenance work and creates more places where behavior can drift.

## What Foundgine contributes

Foundgine provides a shared execution path:

1. **Define business meaning** in an application-owned semantic model.
2. **Resolve caller intent** against declared entities, fields and capabilities.
3. **Authorize the resolved operation** using application-controlled identity and policy.
4. **Plan and bind execution** before handing work to a supported provider.
5. **Record evidence** so operations can be inspected and tested.

The core principle is simple: **generated intent is not generated authority**. An AI agent may request an operation; it does not gain permission merely by asking.

## Why it may be better than adding another interface-specific implementation

Foundgine's intended value is not that it replaces every tool in the stack. It is that it can reduce repeated semantic and execution work across supported interfaces while preserving the systems an enterprise already operates.

| Existing investment | Foundgine's role |
|---|---|
| CRM, ERP and domain applications | Keep ownership of business rules and data |
| EF Core, Hibernate/JPA and databases | Continue handling persistence and physical execution |
| REST, GraphQL and MCP | Provide caller and transport surfaces |
| Identity providers and application policy | Remain the source of identity and authority |
| Temporal and other workflow engines | Continue orchestrating long-running processes |
| Agent frameworks | Continue building and running agents |

Foundgine is complementary infrastructure, not a replacement CRM, ERP, ORM, identity provider, workflow engine, API gateway or agent runtime.

## Business value to measure

Potential benefits include less duplicated implementation, fewer places to update when shared semantics change, more consistent enforcement tests, less integration rework and preservation of existing technology investments.

These are hypotheses to validate in each environment—not guaranteed savings or fixed percentage claims. A credible evaluation should measure:

- engineering hours to expose the same capability through multiple interfaces;
- duplicated policy and validation code;
- effort required to change a shared business rule;
- security-test coverage across caller paths;
- operational cost and latency under equivalent workloads; and
- adoption, hosting and ongoing maintenance costs.

A practical ROI model is:

`Net annual benefit = development savings + maintenance savings + expected risk-cost reduction − adoption and operating costs`

Use measured baseline and pilot data rather than generic market percentages.

## When Foundgine is a fit

Foundgine is most relevant when an organisation has multiple applications or bounded contexts, several interfaces exposing overlapping business capabilities, growing AI-agent access requirements, duplicated authorization or query logic, or a need to add capabilities without replacing existing persistence technology.

It may be unnecessary for a small application with one interface, simple policies and little duplication.

## Explore the implementation

- [Core concepts](core-concepts.md)
- [Semantic model and metadata](semantic-model.md)
- [Planning and execution](planning-execution.md)
- [Authorization and security](authorization-security.md)
- [AI agents and MCP](agents-mcp.md)
- [Cross-language implementation map](implementation-map.md)
- [Product positioning and commercial strategy](../PRODUCT-POSITIONING-AND-COMMERCIAL-STRATEGY.md)
