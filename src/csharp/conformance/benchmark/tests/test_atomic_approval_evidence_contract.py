from pathlib import Path

ROOT = Path(__file__).resolve().parents[5]


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


def test_dotnet_requires_atomic_store_before_returning_approval_receipt():
    source = read("src/csharp/Foundgine.Providers/Gateway/Approvals/FoundgineGatewayApprovalService.cs")
    assert "IAtomicFoundgineGatewayApprovalStore" in source
    assert "requires an atomic approval-plus-audit/outbox store" in source
    assert "StoreWithEvidenceAsync" in source


def test_java_requires_atomic_store_before_returning_approval_receipt():
    source = read("src/java/foundgine-providers/src/main/java/com/foundgine/providers/gateway/approvals/FoundgineGatewayApprovalService.java")
    assert "IAtomicFoundgineGatewayApprovalStore" in source
    assert "requires an atomic approval-plus-audit/outbox store" in source
    assert "storeWithEvidence" in source


def test_both_runtimes_emit_non_secret_plan_bound_approval_evidence():
    csharp = read("src/csharp/Foundgine.Providers/Gateway/Approvals/FoundgineGatewayApprovalEvidence.cs")
    java = read("src/java/foundgine-providers/src/main/java/com/foundgine/providers/gateway/approvals/FoundgineGatewayApprovalEvidence.java")
    assert "ApprovalRequested" in read("src/csharp/Foundgine.Providers/Gateway/Approvals/FoundgineGatewayApprovalService.cs")
    assert "ApprovalRequested" in read("src/java/foundgine-providers/src/main/java/com/foundgine/providers/gateway/approvals/FoundgineGatewayApprovalService.java")
    for source in (csharp, java):
        assert "planFingerprint" in source or "PlanFingerprint" in source
        assert "rawHandle" not in source and "raw_handle" not in source


def test_in_memory_stores_are_explicitly_development_only():
    csharp = read("src/csharp/Foundgine.Providers/Gateway/Approvals/InMemoryFoundgineGatewayApprovalStore.cs")
    java = read("src/java/foundgine-providers/src/main/java/com/foundgine/providers/gateway/approvals/InMemoryFoundgineGatewayApprovalStore.java")
    assert "development" in csharp.lower() and "not durable" in csharp.lower()
    assert "development" in java.lower() and "not durable" in java.lower()
