# Foundgine Supply Chain — Starter Sample

The smallest realistic Foundgine application in this repository.

It demonstrates the complete basic path without splitting the application into a collection of projects:

**MCP → application → semantic model → Foundgine planning/execution → PostgreSQL**

The folders are architectural boundaries; they do not require separate assemblies. The AOT generator sees the whole application project, so the starter stays easy to read and easy to run.

## What it demonstrates

- A small Supply Chain domain and explicit model-to-storage mappings.
- A generated semantic model from the application model.
- Capability authorization before execution.
- MCP as the agent-facing transport.
- Foundgine planning and SQL execution against PostgreSQL.
- Basic read and mutation capabilities, including `place_order` and `cancel_order`.

## Run

Set `SupplyChainConnectionString`, then:

```powershell
dotnet run --project src/csharp/samples/Foundgine.SupplyChain/Foundgine.SupplyChain.csproj
```

The application exposes:

- MCP: `http://localhost:4422/mcp` when run with the supplied container configuration.
- Health: `/health` and `/health/ready`.

For the container image, see `Dockerfile`.

## Where to go next

If you want the full semantic/authorization/retrieval/security case study, use:

`../Foundgine.SupplyChain.Advanced`

The starter intentionally does **not** contain the advanced proving-ground tests or benchmark machinery.
