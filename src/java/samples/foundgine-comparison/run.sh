#!/usr/bin/env bash
# Needs Docker (Postgres only), JDK 21+ and Maven.
#   ./run.sh              start Postgres, both apps (Foundgine :5001, baseline :5002) and run the two-app parity diff
#   ./run.sh check        baseline alone: exact row-level and denial checks
#   ./run.sh foundgine    Foundgine alone: same exact checks against :5001
#   ./run.sh spring       just start the baseline in the foreground
set -euo pipefail
cd "$(dirname "$0")"
mode="${1:-both}"
docker compose up -d --wait
pids=()
trap 'for p in "${pids[@]:-}"; do kill "$p" 2>/dev/null || true; done' EXIT

wait_for() { for _ in $(seq 90); do curl -s -o /dev/null "http://localhost:$1/graphql" && return 0; sleep 1; done; echo "nothing on :$1, see $2" >&2; return 1; }
start_spring() {
  mvn -q -f spring-shop/pom.xml -DskipTests package
  java -jar spring-shop/target/spring-shop-1.0.0.jar >/tmp/spring-shop.log 2>&1 & pids+=($!)
  wait_for 5002 /tmp/spring-shop.log
}
start_foundgine() {
  # The Foundgine module's parent is the Java reactor POM; -am builds core/runtime/providers/extensions first.
  mvn -q -f ../../pom.xml -pl samples/foundgine-comparison/foundgine-shop -am -DskipTests install
  mvn -q -f foundgine-shop/pom.xml exec:java >/tmp/foundgine-shop.log 2>&1 & pids+=($!)
  wait_for 5001 /tmp/foundgine-shop.log
}

case "$mode" in
  spring)    exec mvn -f spring-shop/pom.xml spring-boot:run ;;
  check)     start_spring;    python3 tools/parity.py http://localhost:5002 ;;
  foundgine) start_foundgine; python3 tools/parity.py http://localhost:5001 ;;
  both)      start_spring; start_foundgine; python3 tools/parity.py http://localhost:5001 http://localhost:5002 ;;
  *) echo "unknown mode: $mode" >&2; exit 2 ;;
esac
