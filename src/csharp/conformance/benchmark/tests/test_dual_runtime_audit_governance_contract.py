from pathlib import Path

ROOT = Path(__file__).resolve().parents[5]


def test_both_audit_buffers_expose_matching_operational_metrics():
    csharp = (ROOT / "src/csharp/Foundgine.Runtime/ControlPlane/AuditLog/BufferedDurableAuditLog.cs").read_text()
    java = (ROOT / "src/java/foundgine-runtime/src/main/java/com/foundgine/runtime/controlplane/auditlog/BufferedDurableAuditLog.java").read_text()
    for field in ("QueueDepth", "Accepted", "Persisted", "Failed", "BackpressureWaits"):
        assert field in csharp
    for field in ("queueDepth", "accepted", "persisted", "failed", "backpressureWaits"):
        assert field in java


def test_governors_audit_before_returning_an_allow_result():
    csharp = (ROOT / "src/csharp/Foundgine.Runtime/ControlPlane/ToolCallGovernor.cs").read_text()
    java = (ROOT / "src/java/foundgine-runtime/src/main/java/com/foundgine/runtime/controlplane/ToolCallGovernor.java").read_text()
    assert csharp.index("Audit(AuditCategory.Routed") < csharp.index("return new ToolCallGovernanceResult(PolicyOutcome.Allow")
    assert java.index("AuditEvent.AuditCategory.ROUTED") < java.index("PolicyDecision.PolicyOutcome.ALLOW, decision.reason(), contract, null")
    assert "_auditLog.Record" in csharp
    assert "auditLog.record" in java


def test_approval_audit_atomicity_gap_is_explicitly_documented():
    plan = (ROOT / "src/DUAL-RUNTIME-HARDENING-PLAN.md").read_text()
    assert "Approval creation precedes the `ApprovalRequested` audit" in plan
    assert "transactional approval+audit/outbox transitions" in plan
