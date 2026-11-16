#!/usr/bin/env bash
# R17 restart under load, R18 restart with data (rollback is manual), R19 backup/restore drill (R20, R21 are manual).
source "$(dirname "$0")/../lib.sh"; need docker || die "docker not found"; require_e2e
: "${CRM_DB_PASSWORD:?needed for the CRM-side checks}"
gen; cd "$SUITE"; ERPDB=$(C erp-db); CRMDB=$(C crm-db)
erpq() { docker exec -i "$ERPDB" psql -U erp -d "${2:-erp}" -Atc "$1"; }
crmq() { docker exec -i "$CRMDB" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$CRM_DB_PASSWORD" -h -1 -W -Q "SET NOCOUNT ON; $1" | tr -d '\r' | head -1; }
ERP_TABLES="sales_orders sales_order_lines erp_inventory erp_invoices erp_payments stock_reservations"
counts() { for t in $ERP_TABLES; do echo "erp.$t=$(erpq "select count(*) from $t")"; done; echo "crm.quotes=$(crmq 'select count(*) from quotes')"; }

say "R17: graceful restart of crm-api under load"
OUT=$WORK/e2e-restart; rm -rf "$OUT"; $PY -m fpbench journeys "$CFG/e2e-restart.json" --out "$OUT"
$PY -m fpbench check restart "$OUT" --dir "$EV" --max-error-rate 0.001 || true

say "R18: restart the services with data present"
before=$(counts); orders=$(echo "$before" | sed -n 's/^erp.sales_orders=//p')
if [ "${orders:-0}" -lt 1 ]; then rec R18 not_run --detail "database is empty; run e2e-perf first so the restart is tested on populated data"
else
  docker restart "$(C erp)" "$(C crm-api)" "$(C crm-worker)" "$(C orchestration)" >/dev/null
  wait_up "${ERP_URL:-http://127.0.0.1:8082}/actuator/health" 240 || true; wait_up "${CRM_URL:-http://127.0.0.1:5000}/health" 240 || true; sleep 15
  after=$(counts)
  if ! diff <(echo "$before") <(echo "$after") >/dev/null; then
    rec R18 fail --detail "row counts changed across restart: $(diff <(echo "$before") <(echo "$after") | tr '\n' ' ' | cut -c1-300)"
  elif ! $PY -m fpbench invariants "$CFG/e2e-invariants.json" check >"$WORK/r18-invariants.log" 2>&1; then
    rec R18 fail --detail "invariants broken after restart" --artifact "$WORK/r18-invariants.log"
  elif [ "${ROLLBACK_TESTED:-0}" = 1 ]; then rec R18 pass --detail "restart with data: counts unchanged, invariants hold; rollback tested by hand"
  else rec R18 not_run --measured '{"restart_with_data":"pass"}' --detail "restart part passed; rollback is manual (set ROLLBACK_TESTED=1 after testing it)"; fi
fi

say "R19: backup and restore drill (RTO_S=${RTO_S:-unset} RPO_S=${RPO_S:-unset})"
if [ -z "${RTO_S:-}" ] || [ -z "${RPO_S:-}" ] || [ -z "${CRM_DB_NAME:-}" ]; then
  rec R19 not_run --detail "set RTO_S, RPO_S (agreed objectives) and CRM_DB_NAME so the drill has something to compare against"
else
  ok=1; why=""
  t0=$(date +%s); docker exec "$ERPDB" pg_dump -U erp -Fc erp -f /tmp/erp.dump
  want=$(counts | grep '^erp\.')
  erpq "drop database if exists erp_restore" postgres >/dev/null; erpq "create database erp_restore" postgres >/dev/null
  r0=$(date +%s); docker exec "$ERPDB" pg_restore -U erp -d erp_restore --no-owner /tmp/erp.dump; rto_erp=$(( $(date +%s) - r0 ))
  got=$(for t in $ERP_TABLES; do echo "erp.$t=$(erpq "select count(*) from $t" erp_restore)"; done)
  [ "$want" = "$got" ] || { ok=0; why="ERP restore row counts differ"; }
  erpq "drop database erp_restore" postgres >/dev/null
  # CRM: backup, restore under a new name (files moved), compare quote counts
  docker exec "$CRMDB" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$CRM_DB_PASSWORD" -b -Q "BACKUP DATABASE [$CRM_DB_NAME] TO DISK='/tmp/crm.bak' WITH INIT" >/dev/null || { ok=0; why="$why; CRM backup failed"; }
  mv=$(docker exec "$CRMDB" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$CRM_DB_PASSWORD" -h -1 -W -s '|' -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK='/tmp/crm.bak'" | tr -d '\r' | awk -F'|' 'NF>3{printf ", MOVE N'"'"'%s'"'"' TO N'"'"'/tmp/restore_%s'"'"'", $1, NR}')
  c0=$(date +%s); docker exec "$CRMDB" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$CRM_DB_PASSWORD" -b -Q "RESTORE DATABASE [${CRM_DB_NAME}_restore] FROM DISK='/tmp/crm.bak' WITH REPLACE$mv" >/dev/null || { ok=0; why="$why; CRM restore failed"; }
  rto_crm=$(( $(date +%s) - c0 ))
  qq=$(crmq "select count(*) from [${CRM_DB_NAME}_restore].dbo.quotes"); [ "$qq" = "$(crmq 'select count(*) from quotes')" ] || { ok=0; why="$why; CRM restored quote count $qq differs"; }
  crmq "ALTER DATABASE [${CRM_DB_NAME}_restore] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${CRM_DB_NAME}_restore]" >/dev/null || true
  rto=$(( rto_erp + rto_crm )); [ "$rto" -le "$RTO_S" ] || { ok=0; why="$why; restore took ${rto}s > RTO ${RTO_S}s"; }
  [ "${BACKUP_INTERVAL_S:-999999999}" -le "$RPO_S" ] || { ok=0; why="$why; BACKUP_INTERVAL_S (${BACKUP_INTERVAL_S:-unset}) is not <= RPO_S ($RPO_S): a restore shows no data loss at the dump instant, but RPO also needs a backup schedule that meets it"; }
  rec R19 "$([ $ok = 1 ] && echo pass || echo fail)" --measured "{\"restore_s\":$rto,\"rto_s\":$RTO_S,\"rpo_s\":$RPO_S}" --detail "$why"
fi
say "R20 (version upgrade) and R21 (incident telemetry) are manual: see criteria.json"
