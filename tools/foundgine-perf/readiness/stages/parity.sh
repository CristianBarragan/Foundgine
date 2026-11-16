#!/usr/bin/env bash
# R13: same actor + same request through REST, GraphQL and MCP must give the same decision. Vectors: readiness/parity-vectors.json.
source "$(dirname "$0")/../lib.sh"; need docker || die "docker not found"; require_e2e; gen; cd "$SUITE"
$PY -m fpbench check parity "$CFG/e2e-perf.json" --vectors "${PARITY_VECTORS:-readiness/parity-vectors.json}" --dir "$EV" || true
