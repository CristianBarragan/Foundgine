#!/usr/bin/env python3
"""Foundgine (Scenarios/) vs EF Core comparator (Comparators/EfCore/), non-blank lines between markers.
EF = ENTITIES (persistence classes EF requires) + CONFIG (model + query filters) + MAPPING (entity->domain projection).
Foundgine = CONFIG only (no persistence classes, no projection code). Filters = authorization declarations."""
import glob, os, re
here = os.path.dirname(os.path.abspath(__file__))
def section(text, name):
    m = re.search(rf"// BEGIN {name}\n(.*?)// END {name}", text, re.S)
    return [l for l in m.group(1).splitlines() if l.strip()] if m else []
rows, order = [], ["Simple","Renamed","Flattened","Owned","Complex","InheritanceTph","InheritanceTpt","ManyToMany","Computed","Legacy","MultiTenant"]
for n in order:
    f = open(os.path.join(here, "Scenarios", n + ".cs")).read()
    g = open(os.path.join(here, "Comparators", "EfCore", n + ".cs")).read()
    fc = section(f, "CONFIG"); fauth = len(re.findall(r"\.(?:Tenant|Owner)\(", "\n".join(fc)))
    ent, cfg, mp = section(g, "ENTITIES"), section(g, "CONFIG"), section(g, "MAPPING")
    efauth = len(re.findall(r"HasQueryFilter\(", "\n".join(cfg)))
    rows.append((n, len(fc), fauth, len(ent), len(cfg), len(mp), efauth, sum(len(re.sub(r"\s","",l)) for l in fc), sum(len(re.sub(r"\s","",l)) for l in ent+cfg+mp)))
print(f"{'Scenario':<16}{'Foundgine':>10}{'F.auth':>8} | {'EF ent':>7}{'EF cfg':>7}{'EF map':>7}{'EF total':>9}{'EF filters':>11}")
for r in rows: print(f"{r[0]:<16}{r[1]:>10}{r[2]:>8} | {r[3]:>7}{r[4]:>7}{r[5]:>7}{r[3]+r[4]+r[5]:>9}{r[6]:>11}")
t = [sum(r[i] for r in rows) for i in range(1, 7)]
print(f"{'TOTAL':<16}{t[0]:>10}{t[1]:>8} | {t[2]:>7}{t[3]:>7}{t[4]:>7}{t[2]+t[3]+t[4]:>9}{t[5]:>11}")
print(f"\nNon-whitespace characters (formatting-independent): Foundgine {sum(r[7] for r in rows)}  vs  EF Core {sum(r[8] for r in rows)}  (ratio {sum(r[8] for r in rows)/sum(r[7] for r in rows):.2f}x)")
