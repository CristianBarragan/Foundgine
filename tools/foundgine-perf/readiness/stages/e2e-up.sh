#!/usr/bin/env bash
# Build the patched e2e copy against the checkout under test and bring the stack up (kept running). Records EG4.
source "$(dirname "$0")/../lib.sh"; need_dirs; need docker || die "docker not found"
# Start from a clean stack: a previous run (kept with --keep) would otherwise be reused as is, with its old Keycloak realm,
# old images and old data. The Foundgine Redis/PostgreSQL containers share this compose project name, so no --remove-orphans here.
if [ -f "$WORK/e2e/infrastructure/docker-compose.yml" ]; then
  say "tear down the previous e2e stack"
  ( cd "$WORK/e2e" && COMPOSE_IGNORE_ORPHANS=1 docker compose --env-file .env -f infrastructure/docker-compose.yml -f infrastructure/docker-compose.demo.yml down -v >/dev/null 2>&1 ) || true
fi
say "prepare patched copy"; P=$(bash "$SUITE/readiness/prepare-e2e.sh" "$E2E" "$FG" "$WORK"); echo "$P" > "$WORK/e2e.path"
cd "$P"; log="$WORK/e2e-up.log"; clean="$WORK/e2e-up.clean.log"

# docker/BuildKit progress writes NULs, \r and ANSI codes, so grep reports "binary file matches" and hides the lines.
# Always diagnose from a sanitized copy.
sanitize_log() {
  { tr -d '\000' < "$log" | sed 's/\x1b\[[0-9;?]*[a-zA-Z]//g' | tr '\r' '\n' > "$clean"; } 2>/dev/null || : > "$clean"
}

# demo.sh packs the Foundgine.Providers.AgentFramework nupkg (not on nuget.org) from FOUNDGINE_REPO when nuget-local/ lacks it.
# Without this it exits immediately with "Missing nuget-local/foundgine.providers.agentframework.<ver>.nupkg".
export FOUNDGINE_REPO="$FG"
export FOUNDGINE_COMMIT="${FOUNDGINE_COMMIT:-$(git -C "$FG" rev-parse HEAD 2>/dev/null || true)}"

say "demo.sh --keep (log: $log)"
ok=0
if bash scripts/demo.sh --keep >"$log" 2>&1; then ok=1; fi
sanitize_log
# demo.sh must exit 0 AND the runner must have reported success (grep -a: never trust binary-file detection)
if [ "$ok" = 1 ] && grep -aq E2E_OK "$clean"; then
  require_e2e
  rec EG4 pass --measured "{\"refs\":\"$(tr '\n' ' ' < .readiness-refs)\"}" --threshold "E2E_OK from a clean docker compose up" --artifact "$clean"
else
  say "EG4 diagnosis (log: $log, sanitized: $clean)"
  echo "--- demo.sh exit: $([ "$ok" = 1 ] && echo 'ok (but no E2E_OK in log)' || echo 'non-zero')" >&2
  echo "--- demo.sh / compose output only (container logs and build steps removed), last 40" >&2
  { grep -avE '^[a-zA-Z0-9._-]+-[0-9]+ +\| |^#[0-9]+ |^ *=> |^\[\+\] |^ *(Network|Volume) ' "$clean" | tail -n 40 >&2; } || true
  echo "--- failure-looking lines inside container logs (Temporal excluded, protobuf gencode warning ignored), last 15" >&2
  { grep -aE '^[a-zA-Z0-9._-]+-[0-9]+ +\| ' "$clean" | grep -aviE '^temporal|makeExtensionsImmutable' \
      | grep -aiE 'error|exception|fatal|refused|denied|failed|unhealthy|cannot|could not|exited' | tail -n 15 >&2; } || true
  echo "--- demo.sh stage markers / explicit failure messages" >&2
  { grep -anE '^== |did not|Missing|dependency failed|exited \(|runner did not report' "$clean" | tail -n 10 >&2; } || true
  echo "--- container state" >&2
  { docker compose -f infrastructure/docker-compose.yml -f infrastructure/docker-compose.demo.yml ps -a 2>&1 | tail -n 20 >&2; } || true
  echo "--- unhealthy / exited containers: health probe output + last 40 log lines (protobuf warning removed)" >&2
  { docker compose -f infrastructure/docker-compose.yml -f infrastructure/docker-compose.demo.yml ps -a --format '{{.Name}}|{{.Status}}' 2>/dev/null \
      | grep -aiE 'unhealthy|exited \([1-9]' | cut -d'|' -f1 | while read -r c; do
          echo "### $c" >&2
          docker inspect --format '{{range .State.Health.Log}}probe exit={{.ExitCode}} {{.Output}}{{"\n"}}{{end}}' "$c" 2>/dev/null | tail -n 3 >&2 || true
          docker logs --tail 40 "$c" 2>&1 | grep -av 'makeExtensionsImmutable' | cut -c1-300 >&2 || true
        done; } || true
  rec EG4 fail --detail "demo.sh did not print E2E_OK; last lines: $(tail -n 5 "$clean" | tr '\n' ' ' | cut -c1-300)" --artifact "$clean"; exit 1
fi