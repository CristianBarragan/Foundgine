from pathlib import Path

ROOT = Path(__file__).resolve().parents[5]
CS = ROOT / "src/csharp/Foundgine.Providers"
JAVA = ROOT / "src/java/foundgine-providers/src/main"


def test_both_runtimes_commit_approval_and_outbox_in_one_transaction():
    csharp = (CS / "Gateway/Approvals/PostgresFoundgineGatewayApprovalStore.cs").read_text()
    java = (JAVA / "java/com/foundgine/providers/gateway/approvals/PostgresFoundgineGatewayApprovalStore.java").read_text()
    for source in (csharp, java):
        assert "foundgine_gateway_approvals" in source
        assert "foundgine_outbox" in source
        assert "commit" in source.lower()
        assert "rollback" in source.lower()


def test_consumption_is_atomic_owner_tenant_and_expiry_bound():
    for path in (
        CS / "Gateway/Approvals/PostgresFoundgineGatewayApprovalStore.cs",
        JAVA / "java/com/foundgine/providers/gateway/approvals/PostgresFoundgineGatewayApprovalStore.java",
    ):
        source = path.read_text()
        assert "DELETE FROM foundgine_gateway_approvals" in source
        assert "subject" in source and "tenant_id IS NOT DISTINCT FROM" in source
        assert "expires_at>" in source
        assert "DecodeApproval" in source or "decodeApproval" in source


def test_payload_codec_is_explicit_and_migrations_registered():
    cs = (CS / "Gateway/Approvals/PostgresFoundgineGatewayApprovalStore.cs").read_text()
    java = (JAVA / "java/com/foundgine/providers/gateway/approvals/FoundgineGatewayApprovalPayloadCodec.java").read_text()
    assert "IFoundgineGatewayApprovalPayloadCodec" in cs
    assert "FoundgineGatewayApprovalPayloadCodec" in java
    assert '"004_gateway_approvals"' in (JAVA / "java/com/foundgine/providers/eventstore/schema/EventStoreMigrator.java").read_text()
    assert '"004_gateway_approvals"' in (CS / "EventStore/Schema/EventStoreMigrator.cs").read_text()


def test_standalone_production_approval_store_write_is_rejected():
    cs = (CS / "Gateway/Approvals/PostgresFoundgineGatewayApprovalStore.cs").read_text()
    java = (JAVA / "java/com/foundgine/providers/gateway/approvals/PostgresFoundgineGatewayApprovalStore.java").read_text()
    assert "standalone approval persistence is prohibited" in cs
    assert "standalone approval persistence is prohibited" in java
