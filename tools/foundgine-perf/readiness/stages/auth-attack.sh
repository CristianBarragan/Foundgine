#!/usr/bin/env bash
# R22: forged/missing bearer tokens against protected endpoints. AUTH_ENDPOINTS: space-separated GET URLs that need a token.
source "$(dirname "$0")/../lib.sh"; need docker || die "docker not found"; require_e2e; gen; cd "$SUITE"
: "${AUTH_ENDPOINTS:?set AUTH_ENDPOINTS to protected GET URLs, e.g. 'http://127.0.0.1:5000/api/v1/customers http://127.0.0.1:8082/api/v1/orders/1'}"
args=(); for u in $AUTH_ENDPOINTS; do args+=(--endpoint "$u"); done
$PY -m fpbench check authattack "$CFG/e2e-perf.json" --dir "$EV" "${args[@]}" || true
