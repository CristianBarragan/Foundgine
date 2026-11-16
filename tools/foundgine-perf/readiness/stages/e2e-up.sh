#!/usr/bin/env bash
# Build the patched e2e copy against the checkout under test and bring the stack up (kept running). Records EG4.
source "$(dirname "$0")/../lib.sh"; need_dirs; need docker || die "docker not found"
say "prepare patched copy"; P=$(bash "$SUITE/readiness/prepare-e2e.sh" "$E2E" "$FG" "$WORK"); echo "$P" > "$WORK/e2e.path"
cd "$P"; log="$WORK/e2e-up.log"
say "demo.sh --keep (log: $log)"
if bash scripts/demo.sh --keep >"$log" 2>&1 && grep -q E2E_OK "$log"; then
  require_e2e
  rec EG4 pass --measured "{\"refs\":\"$(tr '\n' ' ' < .readiness-refs)\"}" --threshold "E2E_OK from a clean docker compose up" --artifact "$log"
else
  rec EG4 fail --detail "demo.sh did not print E2E_OK; last lines: $(tail -n 5 "$log" | tr '\n' ' ' | cut -c1-300)" --artifact "$log"; exit 1
fi
