#!/usr/bin/env python3
"""Generate every e2e config for one compose project so container names, URLs and probes cannot drift apart.
  python3 readiness/gen_configs.py --out results/configs [--project foundgine-e2e] ...
Compose names containers <project>-<service>-1, so the project name is the only thing that must match `docker ps`.
  --list-cells PREFIX   prints the fault-matrix cell names (without prefix) that start with PREFIX, one per line (used to build --require lists)"""
import argparse, json, os

SVCS = ["crm-api", "crm-worker", "erp", "orchestration", "temporal", "crm-db", "erp-db", "keycloak"]
PAIRS = [("crm-api", "erp"), ("erp", "temporal"), ("crm-db", "erp-db"), ("orchestration", "temporal"), ("crm-worker", "orchestration")]
NET_TARGETS = ["erp", "crm-api", "temporal", "crm-db", "erp-db", "keycloak"]
CRASH_TARGETS = ("erp", "erp-db", "crm-api", "orchestration", "crm-worker", "temporal")

def build(a):
    P = a.project; C = lambda s: f"{P}-{s}-1"
    py = a.python
    out = os.path.abspath(a.out); os.makedirs(out, exist_ok=True)
    inv_path = os.path.join(out, "e2e-invariants.json")
    inv = {"erp_psql": f"docker exec -i {C('erp-db')} psql -U erp -d erp -At -F '|' -c",
           "crm_sql": f"docker exec -i {C('crm-db')} /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"$CRM_DB_PASSWORD\" -h -1 -W -s '|' -Q",
           "outbox_stuck_s": a.outbox_stuck_s}
    with open(inv_path, "w") as f: json.dump(inv, f, indent=1)
    snap = f"{py} -m fpbench invariants {inv_path} snapshot --snapshot-file {out}/snapshot.json"
    restock = f"docker exec {C('erp-db')} psql -U erp -d erp -qc \"update erp_inventory set on_hand = on_hand + 1000000\""
    reset = f"{restock} && {snap}"
    check = {"name": "invariants", "cmd": f"{py} -m fpbench invariants {inv_path} check --snapshot-file {out}/snapshot.json"}
    base = {"auth": {"keycloak_url": a.keycloak_url, "realm": "enterprise", "client_id": "enterprise-web", "users": ["alice", "dave", "carol"], "password": "dev-password"},
            "crm_url": a.crm_url, "erp_url": a.erp_url, "sku_pool": ["SKU-1", "SKU-2", "SKU-3"], "qty": 1, "unit_price": 100, "wait_timeout_s": 180,
            "sessions": a.sessions, "seed": 20261116, "drain_timeout_s": 300,
            "environment": {"note": a.env_note, "compose_project": P},
            "resources": {s: {"kind": "docker", "containers": [C(s)]} for s in ("crm-api", "erp", "temporal", "orchestration", "crm-worker")},
            "probes": {"erp_outbox_oldest_unpublished_s": f"docker exec {C('erp-db')} psql -U erp -d erp -Atc \"select coalesce(extract(epoch from now()-min(created_at)),0) from erp_temporal_event_outbox where published_at is null\"",
                       "erp_outbox_backlog_rows": f"docker exec {C('erp-db')} psql -U erp -d erp -Atc \"select count(*) from erp_temporal_event_outbox where published_at is null\""}}
    c2c = "fpbench.journeys.foundgine_e2e:customer_to_cash"
    def write(name, cfg):
        with open(os.path.join(out, name), "w") as f: json.dump(cfg, f, indent=1)

    # performance (R4, R6)
    perf = dict(base, duration_s=a.perf_duration, journeys=[
        {"name": "customer-onboarding", "fn": "fpbench.journeys.foundgine_e2e:customer_onboarding", "rates": a.onboarding_rates, "reset_cmd": reset},
        {"name": "customer-to-cash", "fn": c2c, "rates": a.rates, "reset_cmd": reset},
        {"name": "read-paths", "fn": "fpbench.journeys.foundgine_e2e:read_paths", "closed": [1, 8, 32], "duration_s": 30}])
    write("e2e-perf.json", perf)
    over = [round(max(a.rates) * f, 2) for f in (1.25, 1.5, 2.0)]
    write("e2e-overload.json", dict(base, duration_s=a.perf_duration, journeys=[{"name": "customer-to-cash-overload", "fn": c2c, "rates": over, "reset_cmd": reset, "journey_timeout_s": 300}]))

    # staged overload + recovery (R6): normal -> 2x the top rate -> normal
    top = max(a.rates)
    write("e2e-staged.json", dict(base, sessions=1, journeys=[{"name": "customer-to-cash-staged", "fn": c2c, "reset_cmd": reset, "journey_timeout_s": 300,
        "staged": [{"rate": a.fault_rate, "s": 30}, {"rate": top * 2, "s": 60}, {"rate": a.fault_rate, "s": 240}]}]))

    # fault matrix (R7, R8, R9, R10)
    def fault(name, up, down, dur=150, extra=None, cleanup=None):
        j = {"name": name, "fn": c2c, "rates": [a.fault_rate], "duration_s": dur, "reset_cmd": reset, "events": [
                {"at_s": 45, "name": "fault_start", "cmd": up}, {"at_s": 45 + a.fault_s, "name": "fault_end", "cmd": down}],
             "cleanup_cmd": cleanup or down, "post_timeout_s": a.post_timeout_s, "post_retry_s": 5, "post_cmd": [check]}
        if extra: j.update(extra)
        return j
    J = []
    for s in SVCS: J.append(fault(f"kill-{s}", f"docker kill {C(s)}", f"docker start {C(s)}"))
    for x, y in PAIRS: J.append(fault(f"kill-pair-{x}+{y}", f"docker kill {C(x)} {C(y)}", f"docker start {C(x)} {C(y)}"))
    net = f"{P}_default"
    for s in NET_TARGETS:
        # reconnect with the compose service alias, otherwise peers can no longer resolve `<service>` by name
        J.append(fault(f"net-partition-{s}", f"docker network disconnect {net} {C(s)}", f"docker network connect --alias {s} {net} {C(s)}",
                       cleanup=f"docker network connect --alias {s} {net} {C(s)} 2>/dev/null || true"))
        J.append(fault(f"net-latency-{s}", f"docker run --rm --net container:{C(s)} --cap-add NET_ADMIN {a.netshoot} tc qdisc add dev eth0 root netem delay 300ms 100ms",
                       f"docker run --rm --net container:{C(s)} --cap-add NET_ADMIN {a.netshoot} tc qdisc del dev eth0 root || true"))
    # crash mid-flight: three quick kills during steady load (R9)
    for s in CRASH_TARGETS:
        ev = []
        for i, t in enumerate((20, 50, 80)):
            ev += [{"at_s": t, "name": "fault_start" if i == 0 else f"kill_{i+1}", "cmd": f"docker kill {C(s)}"},
                   {"at_s": t + 4, "name": "fault_end" if i == 2 else f"start_{i+1}", "cmd": f"docker start {C(s)}"}]
        J.append({"name": f"crash-{s}", "fn": c2c, "rates": [a.fault_rate], "duration_s": 120, "reset_cmd": reset, "events": ev,
                  "cleanup_cmd": f"docker start {C(s)} || true", "post_timeout_s": a.post_timeout_s, "post_retry_s": 5, "post_cmd": [check]})
    write("e2e-faults.json", dict(base, sessions=a.fault_sessions, journeys=J))

    # soak (R11)
    write("e2e-soak.json", dict(base, sessions=1, duration_s=int(a.soak_hours * 3600), journeys=[
        {"name": "soak", "fn": c2c, "rates": [a.soak_rate], "reset_cmd": reset, "journey_timeout_s": 600, "post_timeout_s": a.post_timeout_s, "post_cmd": [check]}]))

    # tenant isolation (R12) and the background load it runs beside
    write("e2e-tenant.json", dict(base, sessions=1, duration_s=a.tenant_s, journeys=[
        {"name": "tenant-isolation", "fn": "fpbench.journeys.foundgine_e2e:tenant_isolation", "rates": [a.tenant_rate]}]))
    write("e2e-tenant-background.json", dict(base, sessions=1, duration_s=a.tenant_s + 30, journeys=[{"name": "background-c2c", "fn": c2c, "rates": [a.fault_rate]}]))

    # restart under load (R17)
    write("e2e-restart.json", dict(base, sessions=a.sessions, journeys=[
        {"name": "graceful-restart-crm-api", "fn": c2c, "rates": [a.fault_rate], "duration_s": 120, "reset_cmd": reset,
         "events": [{"at_s": 40, "name": "fault_start", "cmd": f"docker stop -t 30 {C('crm-api')}"}, {"at_s": 41, "name": "fault_end", "cmd": f"docker start {C('crm-api')}"}],
         "cleanup_cmd": f"docker start {C('crm-api')} || true", "post_timeout_s": a.post_timeout_s, "post_cmd": [check]}]))
    return out

def cell_names(a):
    """Names of the fault-matrix cells; kept in step with build() by construction (build() is the only writer)."""
    import tempfile
    a = argparse.Namespace(**{**vars(a), "out": tempfile.mkdtemp()})
    build(a)
    with open(os.path.join(a.out, "e2e-faults.json")) as f: return [j["name"] for j in json.load(f)["journeys"]]

def parser():
    e = os.environ.get
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="/tmp/gen-configs"); ap.add_argument("--project", default=e("COMPOSE_PROJECT_NAME", "foundgine-e2e"))
    ap.add_argument("--python", default="python3")
    ap.add_argument("--keycloak-url", default=e("KEYCLOAK_URL", "http://keycloak:8080")); ap.add_argument("--crm-url", default=e("CRM_URL", "http://127.0.0.1:5000")); ap.add_argument("--erp-url", default=e("ERP_URL", "http://127.0.0.1:8082"))
    ap.add_argument("--sessions", type=int, default=int(e("SESSIONS", "5"))); ap.add_argument("--perf-duration", type=int, default=60)
    ap.add_argument("--rates", type=float, nargs="+", default=[1, 5, 10, 20]); ap.add_argument("--onboarding-rates", type=float, nargs="+", default=[5, 20, 50])
    ap.add_argument("--fault-sessions", type=int, default=int(e("FAULT_SESSIONS", "2"))); ap.add_argument("--fault-rate", type=float, default=5); ap.add_argument("--fault-s", type=int, default=60); ap.add_argument("--post-timeout-s", type=int, default=int(e("POST_TIMEOUT_S", "300")))
    ap.add_argument("--outbox-stuck-s", type=int, default=300); ap.add_argument("--netshoot", default="nicolaka/netshoot:v0.13")
    ap.add_argument("--soak-hours", type=float, default=float(e("SOAK_HOURS", "24"))); ap.add_argument("--soak-rate", type=float, default=float(e("SOAK_RATE", "3")))
    ap.add_argument("--tenant-rate", type=float, default=10); ap.add_argument("--tenant-s", type=int, default=150)
    ap.add_argument("--env-note", default=e("ENV_NOTE", "record docker/WSL CPU+memory limits here (compose files set none)"))
    ap.add_argument("--list-cells", metavar="PREFIX")
    return ap

if __name__ == "__main__":
    a = parser().parse_args()
    if a.list_cells is not None:
        print("\n".join(n[len(a.list_cells):] for n in cell_names(a) if n.startswith(a.list_cells)))
    else:
        print(build(a))
