# EF tooling (Starter)

- `python3 tools/gen_migration.py Migrations` regenerates `InitialSupplyChain` (Up/Down), its Designer and the model snapshot from one schema spec (PostgreSQL / Npgsql). Keep the spec in step with `Domain/SupplyChainStorage.cs` and `Domain/PersistenceConfigurations.cs`.
- With a .NET SDK prefer `dotnet ef migrations add <Name> --project samples/Foundgine.SupplyChain` (uses `SupplyChainDesignTimeFactory`; set `SupplyChainConnectionString`).
- The first migration was written without an SDK: run `dotnet build` and `dotnet ef migrations has-pending-model-changes` once. Until then the pending-model-changes warning is suppressed (`SupplyChainEf.UseSupplyChainNpgsql`).
