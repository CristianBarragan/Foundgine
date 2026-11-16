#!/usr/bin/env python3
"""Parse test results so 'tests passed' is a measured count, never an exit code alone.
  parse_results.py trx DIR        -> per-assembly passed/failed/skipped from *.trx (dotnet test --logger trx)
  parse_results.py surefire DIR   -> per-class tests/failures/errors/skipped from TEST-*.xml (Maven Surefire), recursive
Exit 0 only when at least one test ran and nothing failed or was skipped."""
import glob, json, os, sys
import xml.etree.ElementTree as ET

def trx(d):
    out = {}
    for f in glob.glob(os.path.join(d, "**", "*.trx"), recursive=True):
        root = ET.parse(f).getroot(); ns = {"t": root.tag.split("}")[0].strip("{")}
        asm = {}
        for ut in root.iterfind(".//t:UnitTest", ns):
            e = ut.find("t:Execution", ns); m = ut.find("t:TestMethod", ns)
            if e is not None and m is not None: asm[e.get("id")] = os.path.basename(m.get("codeBase") or "?")
        for r in root.iterfind(".//t:UnitTestResult", ns):
            a = out.setdefault(asm.get(r.get("executionId"), "?"), {"passed": 0, "failed": 0, "skipped": 0})
            o = r.get("outcome")
            a["passed" if o == "Passed" else "failed" if o in ("Failed", "Error", "Timeout", "Aborted") else "skipped"] += 1
    return out

def surefire(d):
    out = {}
    for f in glob.glob(os.path.join(d, "**", "TEST-*.xml"), recursive=True):
        s = ET.parse(f).getroot()
        out[s.get("name") or os.path.basename(f)] = {"tests": int(s.get("tests", 0)), "failures": int(s.get("failures", 0)),
                                                     "errors": int(s.get("errors", 0)), "skipped": int(s.get("skipped", 0))}
    return out

def totals(kind, data):
    if kind == "trx":
        t = {k: sum(v[k] for v in data.values()) for k in ("passed", "failed", "skipped")}
        t["ok"] = t["passed"] > 0 and t["failed"] == 0 and t["skipped"] == 0
    else:
        t = {k: sum(v[k] for v in data.values()) for k in ("tests", "failures", "errors", "skipped")}
        t["ok"] = t["tests"] > 0 and t["failures"] == 0 and t["errors"] == 0 and t["skipped"] == 0
    return t

def matching(kind, data, pattern):
    """Units (assemblies / classes) whose name matches the regex and that actually ran something."""
    import re
    ran = (lambda v: v["passed"] > 0) if kind == "trx" else (lambda v: v["tests"] - v["skipped"] > 0)
    return sorted(k for k, v in data.items() if re.search(pattern, k, re.I) and ran(v))

if __name__ == "__main__":
    kind, d = sys.argv[1], sys.argv[2]
    data = (trx if kind == "trx" else surefire)(d)
    t = totals(kind, data)
    if len(sys.argv) > 3: t["matching"] = matching(kind, data, sys.argv[3])
    print(json.dumps({"totals": t, "units": data}))
    sys.exit(0 if t["ok"] else 1)
