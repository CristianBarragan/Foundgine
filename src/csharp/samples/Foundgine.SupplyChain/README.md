# Foundgine Supply Chain — Starter Sample

The smallest realistic Foundgine application in this repository.

It demonstrates the complete basic path without splitting the application into a collection of projects:

**MCP → application facade → Foundgine (semantic reads) / generic repository (writes) → EF Core → PostgreSQL**

The folders are architectural boundaries; they do not require separate assemblies. The fluent model (`SupplyChainModel`) sees the whole application project, so the starter stays easy to read and easy to run.

## What it demonstrates

- Domain classes that are **both** the Foundgine semantic model and the EF Core entity: no storage types, no
  mapping layer, no DTOs (`Domain/Models.cs`).
- Fluent Foundgine configuration only: semantic relationships (`ModelConfigurations.cs`), tables and columns
  (`PersistenceConfigurations.cs`), foreign keys (`Connections.cs`), registered once in `SupplyChainModel`.
  `IdentityEntityMap<T>` from the EF provider replaces the model-to-storage maps.
- One-line registration of everything ORM-side: `services.AddFoundgineEf(metadata, (sp, o) => o.UseNpgsql(...))`
  (EF model, ORM maps, `FoundgineDbContext`, `IFoundgineRepository<T>` per entity, `FoundgineEfReader`). The
  reusable parts live in `Foundgine.Providers.EntityFrameworkCore`, not in this sample.
- Reads are semantic operations (`FoundgineEfReader`); writes call the minimal generic repository directly
  (`Add/Get/List/Update/Delete`, plus `Query()` and `Context` as escape hatches).
- Capability authorization before execution, MCP as the agent-facing transport.
- `place_order` / `cancel_order` (`Application/OrderWorkflows.cs`): repository writes plus a few labelled `[native]`
  PostgreSQL statements (advisory lock, idempotency, `FOR UPDATE SKIP LOCKED`) on the same transaction.

> Not compiled yet (no .NET SDK in the authoring sandbox). `SupplyChain-Starter-Tutorial.md` and
> `Foundgine-SupplyChain-Explained.md` still describe the earlier SQL-provider version.

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
