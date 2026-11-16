# sourced by lib-aa.sh and lib.sh: starts the CoffeeBeanery stack with limits and prepares a config with absolute paths
lib_prepare() {
  need_dirs; need docker || die "docker not found"
  B=$FG/src/csharp/benchmarks/CoffeeBeanery.Performance/compose; [ -f "$B/query.yml" ] || die "missing $B/query.yml"
  DC="docker compose -p coffeebeanery-query -f $B/query.yml -f $SUITE/compose/perf.override.yml"
  say "start library stack (limits + published ports)"; $DC up -d --build postgres database hotchocolate foundgine-cold foundgine-warm
  wait_up http://127.0.0.1:4300/graphql 240 || die "hotchocolate did not come up"; wait_up http://127.0.0.1:4302/graphql/warm 240 || die "foundgine-warm did not come up"
  $PY - "$SUITE" "$WORK" "$DC" "${1:-}" <<'P'
import json, sys, os
suite, work, dc, aa = sys.argv[1:5]
c = json.load(open(f"{suite}/configs/coffeebeanery.json"))
c["scenarios"] = [os.path.normpath(os.path.join(suite, "configs", s)) for s in c["scenarios"]]
for t, svc in (("hotchocolate", "hotchocolate"), ("foundgine", "foundgine-warm")):
    c["targets"][t]["reset_cmd"] = f"{dc} restart {svc} >/dev/null && sleep 5"
if aa:   # A/A: the same system under two names
    c["targets"]["hotchocolate-b"] = json.loads(json.dumps(c["targets"]["hotchocolate"])); del c["targets"]["foundgine"]
c["sessions"] = int(os.environ.get("SESSIONS", c["sessions"]))
json.dump(c, open(f"{work}/lib-{'aa' if aa else 'ab'}.json", "w"), indent=1)
P
}
