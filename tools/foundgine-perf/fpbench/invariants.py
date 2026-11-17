"""Live data-integrity invariants for Foundgine-e2e-example (CRM SQL Server + ERP PostgreSQL).

Each invariant returns the VIOLATING rows; the check passes only when there are none. Run after fault injection
(`post_cmd` in a journey config) or on its own: `python3 -m fpbench invariants configs/e2e-invariants.json check`.

Stock conservation needs a baseline: run `... snapshot` BEFORE the load and `... check` after it.
The shell is only used to reach the databases (docker exec psql / sqlcmd); commands come from the config and may use
${ENV} variables, so the same config works for any compose project name.

Constraints already enforced by the schema (unique order per quote, one invoice per order, idempotency keys, outbox
version uniqueness, reserved <= on_hand) cannot be violated silently: the application would get an error instead.
The checks below cover what constraints cannot: cross-table and cross-system agreement.
"""
import json, os, subprocess, sys
from .io import _rj, _wj

def _sh(cmd, timeout=120):
    r = subprocess.run(os.path.expandvars(cmd), shell=True, capture_output=True, text=True, timeout=timeout)
    if r.returncode != 0:
        raise RuntimeError(f"query failed (rc={r.returncode}): {r.stderr.strip()[:300] or r.stdout.strip()[:300]}")
    return [l.strip() for l in r.stdout.splitlines() if l.strip()]

def erp(cfg, sql):
    return [row.split("|") for row in _sh(f'{cfg["erp_psql"]} "{sql}"')]

def crm(cfg, sql):
    rows = _sh(f'{cfg["crm_sql"]} "SET NOCOUNT ON; {sql}"')
    return [row.split("|") for row in rows if not row.startswith("(")]

ERP_CHECKS = {
    "erp_reserved_equals_open_reservations":
        "select i.tenant_id, i.sku, i.reserved from erp_inventory i left join stock_reservations r on r.tenant_id=i.tenant_id and r.sku=i.sku "
        "and not r.released and not r.consumed group by i.tenant_id, i.sku, i.reserved having i.reserved <> coalesce(sum(r.quantity),0)",
    "erp_order_without_lines":
        "select o.erp_order_id from sales_orders o left join sales_order_lines l on l.erp_order_id=o.erp_order_id where l.erp_order_id is null",
    "erp_open_order_without_reservation":
        "select o.erp_order_id from sales_orders o left join stock_reservations r on r.erp_order_id=o.erp_order_id "
        "where o.status='Open' and r.erp_order_id is null",
    "erp_payments_exceed_invoice":
        "select i.erp_invoice_id from erp_invoices i join erp_payments p on p.erp_invoice_id=i.erp_invoice_id and p.status<>'Reversed' "
        "group by i.erp_invoice_id, i.total having sum(p.amount) > i.total",
    "erp_outbox_stuck":   # nothing may stay unpublished after the post-check drain window
        "select event_id from erp_temporal_event_outbox where published_at is null and created_at < now() - interval '{stuck_s} seconds'",
}

def snapshot(cfg):
    on_hand = {f"{t}|{s}": int(v) for t, s, v in erp(cfg, "select tenant_id, sku, on_hand from erp_inventory")}
    consumed = int(erp(cfg, "select coalesce(sum(quantity),0) from stock_reservations where consumed")[0][0])
    return {"on_hand": on_hand, "consumed_total": consumed}

def run(cfg, snap=None):
    """-> list of {name, violations:[...]}; raises on query errors so a broken probe is never read as 'passed'."""
    out = []
    for name, sql in ERP_CHECKS.items():
        v = erp(cfg, sql.replace("{stuck_s}", str(cfg.get("outbox_stuck_s", 300))))
        out.append({"name": name, "violations": v[:20], "count": len(v)})
    if snap:
        now = snapshot(cfg)
        lost = sum(snap["on_hand"].get(k, 0) - v for k, v in now["on_hand"].items())
        delta = now["consumed_total"] - snap["consumed_total"]
        out.append({"name": "erp_stock_conserved", "count": 0 if lost == delta else 1,
                    "violations": [] if lost == delta else [f"on_hand dropped by {lost} but consumed reservations grew by {delta}"]})
    # cross-system: CRM 'Ordered' quotes <-> ERP orders, both directions
    crm_rows = {(t, q): o for t, q, o in crm(cfg, "select tenant_id, quote_id, erp_order_id from quotes where status='Ordered'")}
    erp_rows = {(t, str(q)): o for t, q, o in erp(cfg, "select tenant_id, crm_quote_id, erp_order_id from sales_orders where status='Open'")}
    miss = [f"quote {k} -> order {o or '<empty>'} missing/mismatched in ERP" for k, o in crm_rows.items() if erp_rows.get(k) != o]
    out.append({"name": "x_crm_ordered_has_matching_erp_order", "count": len(miss), "violations": miss[:20]})
    crm_accepted = {(t, q) for t, q in crm(cfg, "select tenant_id, quote_id from quotes where status in ('Accepted','Ordered')")}
    orph = [f"ERP order {o} for quote {k} has no Accepted/Ordered CRM quote" for k, o in erp_rows.items() if k not in crm_accepted]
    out.append({"name": "x_erp_order_has_crm_quote", "count": len(orph), "violations": orph[:20]})
    return out

def main(argv):
    import argparse
    a = argparse.ArgumentParser(); a.add_argument("config"); a.add_argument("action", choices=["snapshot", "check"])
    a.add_argument("--snapshot-file", default="/tmp/fpbench-invariants-snapshot.json")
    a = a.parse_args(argv)
    cfg = _rj(a.config)
    if a.action == "snapshot":
        _wj(a.snapshot_file, snapshot(cfg)); print(f"snapshot written to {a.snapshot_file}"); return 0
    snap = _rj(a.snapshot_file) if os.path.exists(a.snapshot_file) else None
    res = run(cfg, snap)
    bad = [r for r in res if r["count"]]
    for r in res: print(f"{'FAIL' if r['count'] else 'ok  '} {r['name']}: {r['count']}", *r["violations"][:3])
    if not snap: print("note: no snapshot, stock conservation not checked")
    return 1 if bad else 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
