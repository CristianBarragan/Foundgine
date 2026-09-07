#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIAGRAMS="$ROOT/diagrams"
if command -v plantuml >/dev/null 2>&1; then
  plantuml -tsvg "$DIAGRAMS"/*.puml
elif [[ -n "${PLANTUML_JAR:-}" && -f "${PLANTUML_JAR}" ]]; then
  java -jar "$PLANTUML_JAR" -tsvg "$DIAGRAMS"/*.puml
else
  echo "PlantUML not found. Install the plantuml CLI or set PLANTUML_JAR=/path/to/plantuml.jar." >&2
  exit 1
fi
python3 "$ROOT/scripts/verify-diagrams.py"
