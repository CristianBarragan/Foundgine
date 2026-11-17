#!/usr/bin/env bash
cd "$SUITE" && exec ${PY:-python3} -m fpbench readiness "$EV"
