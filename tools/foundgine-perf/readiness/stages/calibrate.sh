#!/usr/bin/env bash
# R3: loader calibration at the highest planned rate against a null (mock) stack. Run on the benchmark host.
source "$(dirname "$0")/../lib.sh"; cd "$SUITE"
RATE=${CAL_RATE:-$($PY -c 'import sys;sys.path.insert(0,"readiness");import gen_configs as g;a=g.parser().parse_args([]);print(max(a.rates+a.onboarding_rates))')}
rm -f /tmp/mock_e2e_erp_down /tmp/mock_e2e_leak
$PY selftest/mock_e2e.py 18110 & M=$!; trap 'kill $M 2>/dev/null' EXIT; sleep 2
cat > "$WORK/calibrate.json" <<J
{"auth":{"keycloak_url":"http://127.0.0.1:18110","users":["alice","dave","carol"]},"crm_url":"http://127.0.0.1:18110/crm","erp_url":"http://127.0.0.1:18110/erp",
 "sku_pool":["SKU-1"],"sessions":1,"seed":1,"duration_s":30,"drain_timeout_s":30,
 "journeys":[{"name":"customer-to-cash","fn":"fpbench.journeys.foundgine_e2e:customer_to_cash","rates":[$RATE]}]}
J
loader $PY -m fpbench check calibrate "$WORK/calibrate.json" --dir "$EV" --rate "$RATE" --duration 30 || true
