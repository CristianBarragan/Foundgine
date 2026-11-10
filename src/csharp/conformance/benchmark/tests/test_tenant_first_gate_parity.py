"""Step 78: tenant-first gate ordering in both runtimes.

SOURCE-CONTRACT TEST, not behavioral evidence. It proves only that in every
public read entry point of both engines the warrant/tenant gate call textually
precedes any call that resolves names against the semantic model
(SemanticRequestResolver / ReadIntentCompiler). Behavior must be proven by
running the engine suites.
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
CS = ROOT / "csharp/Foundgine.Runtime/Execution/FoundgineEngine.cs"
JV = ROOT / "java/foundgine-runtime/src/main/java/com/foundgine/runtime/FoundgineEngine.java"

SPECS = {
    "csharp": (CS, r"AuthenticateBeforeDiscovery\(", r"new (?:Foundgine\.Core\.Semantic\.Intent\.)?(SemanticRequestResolver|ReadIntentCompiler)\("),
    "java": (JV, r"authenticateBeforeDiscovery\(", r"new (SemanticRequestResolver|ReadIntentCompiler)\("),
}


def _discovery_sites(text, gate_re, sink_re):
    sinks = [m.start() for m in re.finditer(sink_re, text)]
    gates = [m.start() for m in re.finditer(gate_re, text)]
    return sinks, gates


def test_every_resolution_site_is_preceded_by_gate():
    counts = {}
    for name, (path, gate_re, sink_re) in SPECS.items():
        text = path.read_text()
        sinks, gates = _discovery_sites(text, gate_re, sink_re)
        assert sinks, f"{name}: no resolution sites found"
        for sink in sinks:
            prior = [g for g in gates if g < sink]
            assert prior, f"{name}: resolution at offset {sink} has no preceding gate"
            # the nearest gate must be within the same method body (<400 chars)
            assert sink - max(prior) < 400, f"{name}: nearest gate is not adjacent to resolution at {sink}"
        counts[name] = len(sinks)
    assert counts["csharp"] == counts["java"], counts


def test_gate_is_generic_and_checks_subject_and_tenant():
    for name, (path, _, _) in SPECS.items():
        text = path.read_text()
        assert "does not authorize the requested subject or tenant scope" in text, name
        assert re.search(r"(?i)allowedTenants", text), name


def _names(text, pattern):
    return [m.group(1) for m in re.finditer(pattern, text)]


def _snake(name):
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower().replace("__", "_")


def test_behavioral_scenarios_exist_in_both_runtimes():
    cs = (ROOT / "csharp/tests/Foundgine.E2E.Tests/TenantFirstGateTests.cs").read_text()
    jv = (ROOT / "java/foundgine-runtime/src/test/java/com/foundgine/runtime/TenantFirstGateParityTest.java").read_text()
    cs_names = {n.lower() for n in _names(cs, r"public (?:async Task|void) (\w+)\(") if n != "Dispose"}
    jv_names = {_snake(n) for n in _names(jv, r"void (\w+)\(\) throws Exception")}
    assert cs_names, "no C# scenarios found"
    assert cs_names == jv_names, (sorted(cs_names ^ jv_names))


def _norm(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def test_shared_scenario_file_matches_both_runtimes():
    import json

    spec = json.loads((ROOT / "conformance/tenant-first-gate-scenarios.json").read_text())
    ids = [s["id"] for s in spec["scenarios"]]
    assert len(ids) == len(set(ids)), "duplicate scenario ids"
    for s in spec["scenarios"]:
        assert s["expect"] in spec["expectations"], s["id"]
        assert s["path"] in {"dry-run", "read-intent", "approved-execution", "dry-run-then-execute"}, s["id"]

    cs = (ROOT / "csharp/tests/Foundgine.E2E.Tests/TenantFirstGateTests.cs").read_text()
    jv = (ROOT / "java/foundgine-runtime/src/test/java/com/foundgine/runtime/TenantFirstGateParityTest.java").read_text()
    cs_names = {_norm(n) for n in _names(cs, r"public (?:async Task|void) (\w+)\(") if n != "Dispose"}
    jv_names = {_norm(n) for n in _names(jv, r"void (\w+)\(\) throws Exception")}
    spec_names = {_norm(i) for i in ids}
    assert spec_names == cs_names, sorted(spec_names ^ cs_names)
    assert spec_names == jv_names, sorted(spec_names ^ jv_names)

    # The messages the tests and engines rely on must be the ones in the shared file.
    for path in (CS, JV):
        assert spec["gateMessage"] in path.read_text(), path.name
    assert spec["gateMessage"] in cs and spec["gateMessage"] in jv
    assert spec["resolverMarker"] in cs and spec["resolverMarker"] in jv
