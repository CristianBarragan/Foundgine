#!/usr/bin/env bash
# R12: tenant isolation probes (with positive controls) run beside background customer-to-cash load.
source "$(dirname "$0")/../lib.sh"; need docker || die "docker not found"; require_e2e; gen; cd "$SUITE"
docker exec "$(C erp-db)" psql -U erp -d erp -qc "update erp_inventory set on_hand = on_hand + 1000000"
BG=$WORK/e2e-tenant-bg; OUT=$WORK/e2e-tenant; rm -rf "$BG" "$OUT"
$PY -m fpbench journeys "$CFG/e2e-tenant-background.json" --out "$BG" --sessions 1 >"$WORK/tenant-bg.log" 2>&1 & B=$!
sleep 20
$PY -m fpbench journeys "$CFG/e2e-tenant.json" --out "$OUT" --sessions 1
wait $B || true
$PY -m fpbench check tenant "$OUT" --dir "$EV" --min-journeys "${MIN_TENANT_JOURNEYS:-1000}" || true
