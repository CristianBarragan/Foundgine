#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT/docker-compose.postgres.yml"
CONNECTION_STRING="Host=localhost;Port=55432;Database=foundgine_e2e;Username=foundgine;Password=foundgine"

if docker compose version >/dev/null 2>&1; then
  compose() { docker compose -f "$COMPOSE_FILE" "$@"; }
elif command -v docker-compose >/dev/null 2>&1; then
  compose() { docker-compose -f "$COMPOSE_FILE" "$@"; }
else
  echo "ERROR: Docker Compose was not found." >&2
  exit 1
fi

cleanup() {
  echo "Leaving PostgreSQL 17 running. Database/schema lifecycle is owned by PostgreSQL init scripts."
  echo "To deliberately recreate the database from init state:"
  echo "  docker compose -f $COMPOSE_FILE down --volumes --remove-orphans"
}
trap cleanup EXIT INT TERM

echo "Checking Docker..."
docker version >/dev/null

echo "Starting PostgreSQL 17..."
compose up -d postgres

echo "Waiting for PostgreSQL..."
for i in $(seq 1 60); do
  if compose exec -T postgres pg_isready -U foundgine -d foundgine_e2e >/dev/null 2>&1; then
    break
  fi
  if [ "$i" -eq 60 ]; then
    compose ps >&2 || true
    compose logs postgres >&2 || true
    echo "ERROR: PostgreSQL did not become ready in time." >&2
    exit 1
  fi
  sleep 1
done

echo "PostgreSQL version:"
compose exec -T postgres psql -U foundgine -d foundgine_e2e -Atc 'SHOW server_version;'

export FOUNDGINE_POSTGRES_CONNECTION_STRING="$CONNECTION_STRING"

echo "Running PostgreSQL E2E tests..."
dotnet test "$ROOT/tests/Foundgine.E2E.Tests/Foundgine.E2E.Tests.csproj" \
  --configuration Release \
  --filter "FullyQualifiedName~Foundgine.E2E.Tests" \
  --logger "console;verbosity=normal"
