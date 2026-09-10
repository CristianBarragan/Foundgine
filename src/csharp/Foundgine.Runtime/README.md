# Foundgine.Runtime

## Purpose

`Foundgine.Runtime` is the application-facing execution boundary of Foundgine v2. It turns resolved semantic intent into controlled execution while keeping authorization, planning, approvals, routing, and execution policy outside individual providers.

## Small application-facing surface

A normal application should need very little of the runtime API. Optional capabilities are opt-in;
plain Foundgine does not enable grounding, graph retrieval, high assurance, audit evidence, or any
other optional capability unless the application explicitly configures it:

```csharp
services.AddFoundgine(model, authorizationPolicy);

// Or discover the semantic model from structural metadata:
services.AddFoundgine(options =>
{
    options.UseMetadata(metadata);
});

// Resolve through DI
IFoundgineExecutor foundgine = ...;
var result = await foundgine.ExecuteAsync(request);
```

Start with `IFoundgineExecutor`, which intentionally exposes only the two `ExecuteAsync` overloads. Use the full `IFoundgine` interface only when you need capability discovery, dry-run or approval workflows. Dry-run/approval, mutation, discovery,
routing and control-plane APIs are advanced surfaces; they are available when a
scenario needs them, but they are not prerequisites for basic execution.

Provider authors normally work against the contracts in
`Foundgine.Core.Execution` (`IExecutionProvider`, `IProviderPlanCompiler`, and
related provider-neutral plan types) rather than runtime internals.

## What this package provides

- `IFoundgineExecutor` as the minimal two-method execution surface, plus the full `IFoundgine` and mutation APIs for advanced workflows.
- Foundgine read/query and mutation orchestration.
- Dependency-injection registration and runtime options.
- Semantic resolution-to-plan execution coordination.
- Plan approval, dry-run and runtime security/resource validation.
- Provider dispatch and execution orchestration over contracts defined in `Foundgine.Core.Execution`.
- Task routing.
- Control-plane facilities for authority recovery and AI-agent tool-call governance, including registry, risk/policy checks, approvals and audit-related contracts.

## What it does not provide

`Foundgine.Runtime` is not a storage provider and does not host GraphQL or MCP. It also does not supply an LLM client. Concrete integrations are supplied by `Foundgine.Providers` and `Foundgine.Extensions`.

## What is expected from the consumer

The application must compose a semantic model, authorization/security policy and one or more concrete providers. Register the runtime through dependency injection and configure the policies appropriate to the application. The runtime is responsible for enforcing the execution boundary; it does not invent your domain model or storage configuration.

## Install

```bash
dotnet add package Foundgine.Runtime --version 2.0.3
```

## Typical relationship

`Foundgine.Core` → `Foundgine.Runtime` → provider/integration packages. Most applications will use `Foundgine.Runtime` together with `Foundgine.Providers` rather than using the runtime alone.
