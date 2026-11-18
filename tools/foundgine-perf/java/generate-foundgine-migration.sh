#!/usr/bin/env bash
# Generates the Foundgine Java arm's Flyway migration from the SupplyChain fluent model using the
# repository's foundgine-migrations tool (the Java counterpart of csharp/generate-foundgine-migration.sh).
# The Foundgine arm does not copy the SQL: it picks it up from the foundgine-sample-supply-chain dependency.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SAMPLE="$ROOT/src/java/samples/foundgine-supply-chain"
MIGRATIONS="$SAMPLE/src/main/resources/db/migration"
if ! command -v mvn >/dev/null 2>&1; then
  echo "Maven (JDK 27) is required to generate the Foundgine JPA migration" >&2
  exit 127
fi
if compgen -G "$MIGRATIONS/V*__InitialCreate.sql" >/dev/null; then
  echo "Foundgine benchmark migration already exists; refusing to overwrite it" >&2
  exit 3
fi
"$SAMPLE/migrations.sh" migrations add InitialCreate
echo "Generated Foundgine Java migration in $MIGRATIONS"
