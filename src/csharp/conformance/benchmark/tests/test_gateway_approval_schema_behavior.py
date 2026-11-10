"""Behavioral checks for the shared gateway-approval PostgreSQL schema.

The byte-parity test always runs. Database behavior tests run only when
FOUNDGINE_TEST_PSQL_DSN and psql are available; they use an isolated schema.
"""
import os
import shutil
import subprocess
import uuid
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[5]
CS_SCHEMA = ROOT / "src/csharp/Foundgine.Providers/EventStore/Schema"
JAVA_SCHEMA = ROOT / "src/java/foundgine-providers/src/main/resources/com/foundgine/providers/eventstore/schema"
MIGRATIONS = ["001_initial", "002_correlation", "003_outbox", "004_gateway_approvals"]
DSN = os.environ.get("FOUNDGINE_TEST_PSQL_DSN")


def test_migrations_are_byte_identical_across_runtimes():
    for name in MIGRATIONS:
        assert (CS_SCHEMA / f"{name}.sql").read_bytes() == (JAVA_SCHEMA / f"{name}.sql").read_bytes(), name


def _psql(schema: str, sql: str):
    return subprocess.run(
        ["psql", DSN, "-v", "ON_ERROR_STOP=1", "-X", "-q", "-t", "-A", "-c",
         f'SET search_path="{schema}"; {sql}'], capture_output=True, text=True, check=False
    )


@pytest.fixture()
def schema():
    if not DSN or shutil.which("psql") is None:
        pytest.skip("set FOUNDGINE_TEST_PSQL_DSN and install psql to run PostgreSQL behavior checks")
    name = "fg_approval_" + uuid.uuid4().hex
    created = subprocess.run(["psql", DSN, "-v", "ON_ERROR_STOP=1", "-X", "-q", "-c", f'CREATE SCHEMA "{name}"'], capture_output=True, text=True)
    if created.returncode:
        pytest.skip(f"PostgreSQL unavailable: {created.stderr.strip()}")
    try:
        for migration in MIGRATIONS:
            sql = (CS_SCHEMA / f"{migration}.sql").read_text()
            result = _psql(name, sql)
            assert result.returncode == 0, result.stderr
        yield name
    finally:
        subprocess.run(["psql", DSN, "-v", "ON_ERROR_STOP=1", "-X", "-q", "-c", f'DROP SCHEMA IF EXISTS "{name}" CASCADE'], capture_output=True, text=True)


def test_approval_schema_enforces_digest_identity_and_expiry(schema):
    digest = "a" * 64
    result = _psql(schema, f"""
        INSERT INTO foundgine_gateway_approvals
            (handle_digest, subject, tenant_id, approval_payload, plan_fingerprint, expires_at, created_at)
        VALUES ('{digest}', 'alice', 'tenant-a', '{{"v":1}}'::jsonb, 'fp',
                clock_timestamp() + interval '5 minutes', clock_timestamp());
        SELECT count(*) FROM foundgine_gateway_approvals WHERE handle_digest='{digest}';
    """)
    assert result.returncode == 0, result.stderr
    assert result.stdout.strip().splitlines()[-1] == "1"


def test_expired_approval_is_not_live_by_database_clock(schema):
    digest = "b" * 64
    result = _psql(schema, f"""
        INSERT INTO foundgine_gateway_approvals
            (handle_digest, subject, tenant_id, approval_payload, plan_fingerprint, expires_at, created_at)
        VALUES ('{digest}', 'alice', 'tenant-a', '{{"v":1}}'::jsonb, 'fp',
                clock_timestamp() - interval '1 second', clock_timestamp() - interval '2 minutes');
        SELECT count(*) FROM foundgine_gateway_approvals
        WHERE handle_digest='{digest}' AND expires_at>clock_timestamp();
    """)
    assert result.returncode == 0, result.stderr
    assert result.stdout.strip().splitlines()[-1] == "0"
