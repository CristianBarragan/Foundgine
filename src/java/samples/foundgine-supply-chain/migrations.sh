#!/usr/bin/env sh
# EF-style migrations for this sample. Examples:
#   ./migrations.sh migrations add AddSupplierEmail
#   ./migrations.sh migrations list
#   ./migrations.sh migrations has-pending-model-changes
#   ./migrations.sh database update
cd "$(dirname "$0")" || exit 1
export FOUNDGINE_JDBC_URL="${FOUNDGINE_JDBC_URL:-${SUPPLYCHAIN_JDBC_URL:-jdbc:postgresql://localhost:4429/foundgine_supply_chain}}"
export FOUNDGINE_JDBC_USER="${FOUNDGINE_JDBC_USER:-${SUPPLYCHAIN_JDBC_USER:-benchmark}}"
export FOUNDGINE_JDBC_PASSWORD="${FOUNDGINE_JDBC_PASSWORD:-${SUPPLYCHAIN_JDBC_PASSWORD:-benchmark}}"
exec mvn -q compile org.codehaus.mojo:exec-maven-plugin:3.5.0:java \
  -Dexec.mainClass=com.foundgine.migrations.MigrationsCli \
  -Dexec.classpathScope=test \
  -Dexec.args="--project . $*"
