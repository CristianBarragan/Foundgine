#!/usr/bin/env bash
# Proves the harness end to end on mock servers. Numbers are NOT Foundgine performance.
set -euo pipefail
cd "$(dirname "$0")/.."
python3 selftest/mock_server.py 18081 8 & A=$!
python3 selftest/mock_server.py 18082 2 & B=$!
python3 selftest/mock_server.py 18083 2 bad & C=$!
trap 'kill $A $B $C 2>/dev/null' EXIT
sleep 2
cat > /tmp/selftest.json <<JSON
{"scenarios":["$PWD/selftest/scenario.json"],"closed_concurrency":[1,4],"open_loop_fractions":[0.5,0.9],
 "sessions":3,"measure_s":1.5,"warmup":{"min_s":1,"max_s":6,"cv":0.25},
 "targets":{"baseline":{"url":"http://127.0.0.1:18081","resources":{"kind":"pid","pids":[$A]}},
            "subject":{"url":"http://127.0.0.1:18082","resources":{"kind":"pid","pids":[$B]}}}}
JSON
rm -rf /tmp/st-ok && python3 -m fpbench run /tmp/selftest.json --out /tmp/st-ok --seed 1
python3 -m fpbench report /tmp/st-ok --baseline baseline --subject subject || true
# negative test: subject returns different bodies -> equivalence must fail
sed "s/18082/18083/; s/\"pids\":\[$B\]/\"pids\":[$C]/" /tmp/selftest.json > /tmp/selftest-bad.json
rm -rf /tmp/st-bad && python3 -m fpbench run /tmp/selftest-bad.json --out /tmp/st-bad --sessions 1 --seed 1 > /tmp/st-bad.log; head -3 /tmp/st-bad.log
python3 -m fpbench report /tmp/st-bad --baseline baseline --subject subject || true
