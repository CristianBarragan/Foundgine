import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[5]
CS = ROOT / "src/csharp/tests/Foundgine.EventStore.Tests/PostgresGatewayApprovalStoreIntegrationTests.cs"
JAVA = ROOT / "src/java/foundgine-providers/src/test/java/com/foundgine/providers/gateway/approvals/PostgresFoundgineGatewayApprovalStoreIntegrationTest.java"
CS_STORE = ROOT / "src/csharp/Foundgine.Providers/Gateway/Approvals/PostgresFoundgineGatewayApprovalStore.cs"
JAVA_STORE = ROOT / "src/java/foundgine-providers/src/main/java/com/foundgine/providers/gateway/approvals/PostgresFoundgineGatewayApprovalStore.java"


def norm(name: str) -> str:
    return name.replace("_", "").lower()


def java_scenarios():
    return {norm(m) for m in re.findall(r"@Test\s+void\s+(\w+)\(", JAVA.read_text())}


def cs_scenarios():
    return {norm(m) for m in re.findall(r"\[Fact\]\s+public async Task (\w+)\(", CS.read_text())}


def test_suites_exist_and_are_non_trivial():
    assert len(java_scenarios()) >= 18
    assert len(cs_scenarios()) >= 18


def test_every_java_scenario_has_a_dotnet_counterpart():
    assert java_scenarios() - cs_scenarios() == set()


def test_required_scenario_families_are_covered_in_both_runtimes():
    required = ["rollsbackapproval", "concurrenttake", "isolation", "expiry", "revocation",
                "decodefailure", "restart", "reservedpartition", "purgeexpired", "malformedlookup"]
    for names in (java_scenarios(), cs_scenarios()):
        for needle in required:
            assert any(needle in n for n in names), needle


def test_java_suite_uses_existing_opt_in_convention_and_isolated_schema():
    text = JAVA.read_text()
    assert "FOUNDGINE_TEST_PG_URL" in text and "Assumptions.assumeTrue" in text
    assert "CREATE SCHEMA" in text and "DROP SCHEMA IF EXISTS" in text


def test_dotnet_suite_uses_testcontainers_postgres():
    text = CS.read_text()
    assert "Testcontainers.PostgreSql" in text and "EventStoreMigrator.ApplyAsync" in text


def test_hardening_rules_exist_in_both_stores():
    for path in (CS_STORE, JAVA_STORE):
        text = path.read_text()
        assert "__foundgine_global__" in text
        assert "purge" in text.lower()
        assert re.search(r"0-9A-Fa-f\]\{64\}", text), "digest format validation"
        assert re.search(r"0-9A-Fa-f\]\{32\}", text), "event id format validation"


def test_outbox_key_never_uses_the_handle_digest():
    cs = CS_STORE.read_text()
    java = JAVA_STORE.read_text()
    assert 'AddWithValue("key", handleDigest)' not in cs
    assert "p.setString(4, digest)" not in java
