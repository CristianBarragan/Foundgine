#!/usr/bin/env bash
# R13/R14: counts from the repo's existing security / gateway / MCP / approval suites. These do NOT close R13/R14 by themselves:
# the cross-surface parity test and the Redis-down test against the live stack are not written, so the rows stay not_run.
source "$(dirname "$0")/../lib.sh"; need_dirs; need dotnet || die "dotnet not found"; need mvn || die "mvn not found"
RES=$WORK/existing; rm -rf "$RES"; mkdir -p "$RES"
n=0; for p in $(find "$FG/src/csharp" -name '*.csproj' | grep -Ei '(security|gateway|mcp|approval|warrant|authority).*test|test.*(security|gateway|mcp|authority)'); do
  n=$((n+1)); dotnet test "$p" -c Release --logger trx --results-directory "$RES/trx" >"$RES/dotnet-$n.log" 2>&1 || true; done
T=$($PY "$SUITE/readiness/parse_results.py" trx "$RES/trx" 2>/dev/null || true)
(cd "$FG/src/java" && mvn -B verify -Dtest='*Security*,*Approval*,*Warrant*,*Authoriz*' -Dsurefire.failIfNoSpecifiedTests=false >"$RES/mvn.log" 2>&1) || true
S=$($PY "$SUITE/readiness/parse_results.py" surefire "$FG/src/java" 2>/dev/null || true)
d="projects=$n; dotnet: ${T:-none}; java: ${S:-none}"
for id in R13 R14; do [ "$id" = R13 ] && [ -f "$EV/R13.json" ] && continue; rec $id not_run --detail "existing suites ran ($(echo "$d" | cut -c1-400)); close R13 with --stage parity, R14 live Redis-down test is not written" --artifact "$RES"; done
