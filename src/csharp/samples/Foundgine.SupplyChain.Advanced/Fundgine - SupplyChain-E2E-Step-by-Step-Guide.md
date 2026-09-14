Foundgine Supply Chain E2E — Step-by-Step Run Guide
Purpose: validate the complete AI-agent-to-PostgreSQL banking-style supply-chain E2E through MCP, Foundgine semantics, authorization, planning, execution, Npgsql, and PostgreSQL.
Target Architecture
AI Agent → MCP → Foundgine capability boundary → Semantic Model → Authorization → Planner → Execution → Npgsql → PostgreSQL → Result/Evidence

Step 0 — Start Clean

Open PowerShell and verify the repository:
cd C:\Foundgine\samples\Foundgine.SupplyChain.Advanced
Then inspect the directory:
Get-ChildItem
Expected entries include: Database, Agent, Semantic/Api/Mcp, docker-compose.yml, run-supply-chain.ps1, and README.md.

Step 1 — Verify .NET

Run:
dotnet --version
dotnet --info
A .NET 9 SDK should be available.

Step 2 — Verify Docker

Run:
docker --version
docker compose version
docker ps

Step 3 — Verify Supply Chain Project Files

Run:
Test-Path .\Database\Database.csproj
Test-Path .\Agent\Agent.csproj
Test-Path .\Semantic/Api/Mcp\Semantic/Api/Mcp.csproj
Test-Path .\docker-compose.yml
Test-Path .\run-supply-chain.ps1
All five commands should return True.

Step 4 — Build Database Project

Run:
dotnet build .\Database\Database.csproj -c Release
Expected: Build succeeded. If it fails, stop and fix this layer before continuing.

Step 5 — Build Agent Project

Run:
dotnet build .\Agent\Agent.csproj -c Release
Expected: Build succeeded.

Step 6 — Build MCP + Foundgine

Run:
dotnet build .\Semantic/Api/Mcp\Semantic/Api/Mcp.csproj -c Release
Expected: Build succeeded.

Step 7 — Validate Docker Compose

Run:
docker compose config
This must resolve the SupplyChain docker-compose.yml without a 'file not found' error.

Step 8 — Start PostgreSQL Only

Run:
docker compose up -d postgres
Then:
docker compose ps
docker compose logs postgres --tail 50
PostgreSQL must be healthy/ready before continuing.

Step 9 — Seed PostgreSQL

Run:
dotnet run --project .\Database\Database.csproj -c Release
This should connect to PostgreSQL, create the schema, and seed suppliers, categories, products, warehouses, inventory, customers, orders, and related data.

Step 10 — Verify Database Layer

Run:
docker compose ps
docker compose logs postgres --tail 30
At this point the database path is proven: Database project → PostgreSQL → schema → seed data.

Step 11 — Start MCP + Foundgine

Run:
docker compose up -d
Then:
docker compose ps
docker compose ps -a

Step 12 — Inspect MCP

Run:
docker compose logs mcp-foundgine --tail 100
Check for startup failures, connection failures, unhandled exceptions, or HTTP 500 errors.

Step 13 — Run the Seeded E2E Agent

Set deterministic benchmark parameters:
$env:SUPPLY_CHAIN_CUSTOMERS="5"
$env:SUPPLY_CHAIN_STEPS="25"
$env:SUPPLY_CHAIN_SEED="20260823"
Then run:
dotnet run --project .\Agent\Agent.csproj -c Release

Step 14 — Run the Convenience Script

Only after the individual projects work, run the full orchestration script:
./run-supply-chain.ps1

Step 15 — What a Successful E2E Must Demonstrate

•	AI agent generates multiple supply-chain requests.
•	Requests travel through MCP.
•	Foundgine resolves semantic capabilities.
•	Identity and permissions are applied.
•	Authorization is enforced before execution.
•	Queries and mutations are planned by Foundgine.
•	Execution reaches PostgreSQL through Npgsql.
•	Successful mutations produce correct database state.
•	Unauthorized operations are rejected without modifying PostgreSQL.
•	PlaceOrder is atomic and validates inventory and pricing server-side.
•	Idempotent retries do not create duplicate orders.
•	Execution evidence can explain the resulting operation.

First Run Protocol

Do not jump directly to the full benchmark if something fails. Validate each layer in order. The first command sequence to execute is Steps 0–3. Once those pass, continue one step at a time. This isolates path, SDK, Docker, project, database, MCP, Foundgine, and agent failures instead of stacking multiple failures together.
Initial PowerShell Block
Use this exact block for the first validation:
cd C:\Foundgine
git status

cd .\samples\Foundgine.SupplyChain.Advanced

Get-ChildItem

dotnet --version

docker --version
docker compose version

Test-Path .\Database\Database.csproj
Test-Path .\Agent\Agent.csproj
Test-Path .\Semantic/Api/Mcp\Semantic/Api/Mcp.csproj
Test-Path .\docker-compose.yml
Test-Path .\run-supply-chain.ps1
