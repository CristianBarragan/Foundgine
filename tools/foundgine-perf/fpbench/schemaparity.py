"""Schema + migration provenance gate.

Two questions, both answered from the LIVE database each arm ended up with:

 1. PROVENANCE. Did the arm get its schema from the migration implementation it is supposed to be measuring?
      foundgine-* arms  -> Foundgine's migrations (C#: the EF migration set built from the Foundgine model; Java: foundgine-migrations
                           generated V<timestamp>__<Name>.sql applied by Flyway)
      baseline arms     -> a plain EF Core migration (`dotnet ef migrations add`) / a plain hand-written Flyway script
    The arm config says which history table and which migration ids/scripts it must show (`expect_history`, regexes) and which it
    must not (`forbid_history`). An arm built with EnsureCreated or Hibernate ddl-auto leaves NO history and fails (`kind: none`).
 2. PARITY. Arms of one runtime (`group`) must end up with the same tables, columns, types, nullability, keys, FKs and indexes,
    otherwise the comparison measures different databases. Constraint/index NAMES are ignored on purpose.

Each arm supplies `schema_cmd`, a shell command that prints the output of scenarios/schema_dump.sql for that arm's database."""
import json, os, re, subprocess
from .io import _rj, _wj, _wt

def load(arm_cfg, runner=subprocess.run):
    p = runner(arm_cfg["schema_cmd"], shell=True, capture_output=True, text=True, timeout=arm_cfg.get("timeout_s", 60))
    if p.returncode != 0:
        raise RuntimeError(f"schema_cmd failed ({p.returncode}): {p.stderr.strip()[:300]}")
    return json.loads(p.stdout.strip().splitlines()[-1])

def _norm(doc):
    return {"columns": sorted(map(tuple, doc["columns"])),
            "keys": sorted((k[0], k[1], tuple(k[2] or [])) for k in doc["keys"]),
            "fks": sorted((f[0], f[1], tuple(f[2] or [])) for f in doc["fks"]),
            "indexes": sorted((i[0], tuple(i[1] or [])) for i in doc["indexes"])}

def diff(a, b):
    """Human-readable differences between two schema docs (empty list = same schema)."""
    na, nb, out = _norm(a), _norm(b), []
    for part in ("columns", "keys", "fks", "indexes"):
        sa, sb = set(na[part]), set(nb[part])
        out += [f"{part}: only in first: {x}" for x in sorted(sa - sb)] + [f"{part}: only in second: {x}" for x in sorted(sb - sa)]
    return out

def provenance(doc, cfg):
    h = doc.get("history") or {"kind": "none", "applied": []}
    want = cfg.get("history_kind")
    errs = []
    if h["kind"] == "none":
        errs.append("no migration history table: the schema was not created by migrations (EnsureCreated / ddl-auto / hand SQL)")
    elif want and h["kind"] != want:
        errs.append(f"history is {h['kind']!r}, expected {want!r}")
    if not h["applied"] and h["kind"] != "none":
        errs.append("migration history is empty")
    for rx in cfg.get("expect_history", []):
        if not any(re.search(rx, x) for x in h["applied"]):
            errs.append(f"no applied migration matches /{rx}/ (applied: {h['applied']})")
    for rx in cfg.get("forbid_history", []):
        hit = [x for x in h["applied"] if re.search(rx, x)]
        if hit:
            errs.append(f"applied migration {hit} matches forbidden /{rx}/ (wrong tool for this arm)")
    return errs

def run(cfg_path, outdir, runner=subprocess.run):
    cfg = _rj(cfg_path)
    arms = {n: t for n, t in cfg["targets"].items() if t.get("schema_cmd")}
    failures, docs = [], {}
    for n, t in arms.items():
        try:
            docs[n] = load(t, runner)
        except Exception as e:
            failures.append(f"{n}: cannot read schema: {e}")
            continue
        failures += [f"{n}: {e}" for e in provenance(docs[n], t)]
    groups = {}
    for n in docs:
        groups.setdefault(arms[n].get("group", "default"), []).append(n)
    for g, names in groups.items():
        for other in names[1:]:
            failures += [f"{g}: {names[0]} vs {other}: {d}" for d in diff(docs[names[0]], docs[other])]
        if len(names) < 2:
            failures.append(f"group {g!r} has {len(names)} arm(s) with a schema_cmd; a comparison needs at least 2")
    if not arms:
        failures.append("no target declares schema_cmd")
    rep = {"pass": not failures, "arms": sorted(arms), "groups": groups, "failures": failures,
           "history": {n: docs[n]["history"] for n in docs}}
    os.makedirs(outdir, exist_ok=True)
    _wj(os.path.join(outdir, "schemaparity.json"), rep)
    _wt(os.path.join(outdir, "schemaparity.md"), "# Migration provenance and schema parity\n\n" + ("**PASS**" if rep["pass"] else "**FAIL**") +
        "\n\n" + "".join(f"- {f}\n" for f in failures))
    return rep
