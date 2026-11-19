#!/usr/bin/env python3
"""Counts non-blank lines between '// BEGIN CONFIG' and '// END CONFIG' in each Scenarios/*.cs file.
Authorization lines = lines containing .Tenant( or .Owner( (declared on the domain, never repeated per table)."""
import glob, os, re
rows = []
for f in sorted(glob.glob(os.path.join(os.path.dirname(__file__), "Scenarios", "*.cs"))):
    s = open(f).read()
    body = s.split("// BEGIN CONFIG")[1].split("// END CONFIG")[0]
    lines = [l for l in body.splitlines() if l.strip()]
    auth = sum(1 for l in lines if re.search(r"\.(Tenant|Owner)\(", l))
    tables = len(re.findall(r'\.Table\(|\.Child<', body))
    rows.append((os.path.basename(f)[:-3], len(lines), auth, tables))
print(f"{'Scenario':<16}{'Config lines':>13}{'Auth lines':>12}{'Tables':>8}")
for r in rows: print(f"{r[0]:<16}{r[1]:>13}{r[2]:>12}{r[3]:>8}")
print(f"{'TOTAL':<16}{sum(r[1] for r in rows):>13}{sum(r[2] for r in rows):>12}{sum(r[3] for r in rows):>8}")
