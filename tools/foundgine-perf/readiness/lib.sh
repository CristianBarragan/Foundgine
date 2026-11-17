# Shared helpers (source me). Requires: SUITE (suite root), EV (evidence dir).
set -euo pipefail
: "${SUITE:?}" "${EV:?}"
mkdir -p "$EV"
PY=${PY:-python3}
export COMPOSE_PROJECT_NAME=${COMPOSE_PROJECT_NAME:-foundgine-e2e}
WORK=${WORK:-$SUITE/results/work}; mkdir -p "$WORK"
CFG=$WORK/configs
say()  { printf '\n== %s\n' "$*" >&2; }
die()  { printf '\nERROR: %s\n' "$*" >&2; exit 2; }
# rec ID STATUS [--measured JSON] [--threshold T] [--detail D] [--source S] [--artifact P] [--owner O]
rec()  { local id=$1 st=$2; shift 2; (cd "$SUITE" && $PY -m fpbench evidence "$id" "$st" --dir "$EV" --source auto "$@") >&2; }
need() { command -v "$1" >/dev/null 2>&1; }
C()    { echo "${COMPOSE_PROJECT_NAME}-$1-1"; }
have_container() { docker ps --format '{{.Names}}' | grep -qx "$1"; }
require_containers() { for c in "$@"; do have_container "$c" || die "container '$c' is not running (docker ps). Check COMPOSE_PROJECT_NAME=$COMPOSE_PROJECT_NAME."; done; }
# any HTTP answer (even 4xx) means the port is up
wait_up() { local url=$1 end=$((SECONDS + ${2:-240})); until [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$url" 2>/dev/null)" != 000 ]; do [ $SECONDS -lt $end ] || return 1; sleep 3; done; }
require_e2e() { require_containers "$(C crm-api)" "$(C erp)" "$(C erp-db)" "$(C crm-db)" "$(C temporal)" "$(C orchestration)" "$(C crm-worker)" "$(C keycloak)"; }
gen() { (cd "$SUITE" && $PY readiness/gen_configs.py --out "$CFG" "$@") >/dev/null; }
# first-class guard so a stage that needs the checkout fails with a clear message
need_dirs() { FG=${FOUNDGINE_DIR:?set FOUNDGINE_DIR to the Foundgine checkout under test}; E2E=${E2E_DIR:?set E2E_DIR to the Foundgine-e2e-example checkout}; [ -d "$FG" ] && [ -d "$E2E" ] || die "FOUNDGINE_DIR/E2E_DIR do not exist"; }
loader() { if [ -n "${LOADER_CPUS:-}" ] && need taskset; then taskset -c "$LOADER_CPUS" "$@"; else "$@"; fi; }
