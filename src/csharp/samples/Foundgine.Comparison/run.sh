#!/usr/bin/env bash
# Required setup: Docker (Postgres only) and the .NET 10 SDK. Nothing else to install or configure.
#   ./run.sh          start Postgres, run Foundgine.Shop on :5001 (GraphQL + MCP)
#   ./run.sh parity   start both apps and diff their GraphQL answers (needs python3, stdlib only)
set -euo pipefail
cd "$(dirname "$0")"
docker compose up -d --wait

if [[ "${1:-}" == "parity" ]]; then
  dotnet build -v q Foundgine.Shop HotChocolate.Shop
  pids=()
  trap 'kill "${pids[@]}" 2>/dev/null || true' EXIT
  Mcp__Enabled=true dotnet run --no-build --project Foundgine.Shop    --urls http://localhost:5001 >/tmp/foundgine-shop.log 2>&1 & pids+=($!)
  dotnet run --no-build --project HotChocolate.Shop --urls http://localhost:5002 >/tmp/hotchocolate-shop.log 2>&1 & pids+=($!)
  for u in 5001 5002; do for _ in $(seq 60); do curl -s -o /dev/null "http://localhost:$u/graphql" && break; sleep 1; done; done
  python3 tools/parity.py http://localhost:5001 http://localhost:5002
  python3 tools/mcp_smoke.py http://localhost:5001
else
  Mcp__Enabled=true exec dotnet run --project Foundgine.Shop --urls http://localhost:5001
fi
