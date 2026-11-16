#!/usr/bin/env bash
# R16: dependency vulnerabilities, secrets, SBOM. A missing scanner means not_run, never pass. IND2 (independent review) is manual.
source "$(dirname "$0")/../lib.sh"; need_dirs; RES=$WORK/scans; rm -rf "$RES"; mkdir -p "$RES"
missing=(); bad=()
if need dotnet; then
  for s in $(find "$FG/src/csharp" -maxdepth 2 \( -name '*.sln' -o -name '*.slnx' \) | head -1); do
    dotnet list "$s" package --vulnerable --include-transitive >"$RES/dotnet-vuln.txt" 2>&1 || true
    grep -Eq '^\s*>.*\b(High|Critical)\b' "$RES/dotnet-vuln.txt" && bad+=("nuget high/critical"); done
else missing+=(dotnet); fi
if need osv-scanner; then osv-scanner --recursive --format json "$FG" >"$RES/osv.json" 2>"$RES/osv.err" || true
  $PY - "$RES/osv.json" <<'P' || bad+=("osv high/critical (CVSS >= 7)")
import json, sys
d = json.load(open(sys.argv[1])); n = 0
for r in d.get("results", []):
    for p in r.get("packages", []):
        for g in p.get("groups", []):
            try: n += float(g.get("max_severity") or 0) >= 7
            except ValueError: pass
sys.exit(1 if n else 0)
P
else missing+=(osv-scanner); fi
if need gitleaks; then gitleaks detect --source "$FG" --no-banner -f json -r "$RES/gitleaks.json" >/dev/null 2>&1 || bad+=("secrets found"); else missing+=(gitleaks); fi
if need syft; then syft "dir:$FG" -o cyclonedx-json="$RES/sbom.cdx.json" >/dev/null 2>&1 || missing+=("syft(failed)"); else missing+=(syft); fi
if [ ${#bad[@]} -gt 0 ]; then rec R16 fail --detail "${bad[*]}" --artifact "$RES"
elif [ ${#missing[@]} -gt 0 ]; then rec R16 not_run --detail "scanners missing or failed: ${missing[*]}" --artifact "$RES"
else rec R16 pass --detail "no high/critical dependency findings, no secrets, SBOM at $RES/sbom.cdx.json" --artifact "$RES"; fi
