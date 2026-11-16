#!/usr/bin/env bash
# Proves the journey runner on a mock CRM/ERP/Keycloak. Numbers are NOT Foundgine performance.
set -euo pipefail
cd "$(dirname "$0")/.."
rm -f /tmp/mock_e2e_erp_down
python3 selftest/mock_e2e.py 18100 & M=$!
trap 'kill $M 2>/dev/null; rm -f /tmp/mock_e2e_erp_down' EXIT
sleep 2
cat > /tmp/selftest-journeys.json <<JSON
{"auth":{"keycloak_url":"http://127.0.0.1:18100","users":["alice","dave"]},
 "crm_url":"http://127.0.0.1:18100/crm","erp_url":"http://127.0.0.1:18100/erp","sku_pool":["SKU-1","SKU-2"],
 "sessions":2,"seed":7,"duration_s":4,"drain_timeout_s":15,
 "resources":{"stack":{"kind":"pid","pids":[$M]}},
 "probes":{"outbox_lag_s":"echo 1"},
 "journeys":[
  {"name":"customer-onboarding","fn":"fpbench.journeys.foundgine_e2e:customer_onboarding","rates":[3,10]},
  {"name":"customer-to-cash","fn":"fpbench.journeys.foundgine_e2e:customer_to_cash","rates":[3,10]},
  {"name":"c2c-fault","fn":"fpbench.journeys.foundgine_e2e:customer_to_cash","rates":[4],"duration_s":7,
   "events":[{"at_s":2,"name":"fault_start","cmd":"touch /tmp/mock_e2e_erp_down"},{"at_s":4,"name":"fault_end","cmd":"rm -f /tmp/mock_e2e_erp_down"}],"post_timeout_s":5,"post_cmd":[{"name":"invariants","cmd":"true"}]}
 ]}
JSON
rm -rf /tmp/jt && python3 -m fpbench journeys /tmp/selftest-journeys.json --out /tmp/jt
python3 -m fpbench journey-report /tmp/jt --slo-p99-s 5 || true
