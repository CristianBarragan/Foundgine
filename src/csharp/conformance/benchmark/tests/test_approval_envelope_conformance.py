"""Approval payload envelope: reference-vector integrity and runtime source-contract checks.

Behavioural evidence for each runtime comes from the runtimes' own conformance tests, which read the same vectors:
  Java: ApprovalPayloadEnvelopeConformanceTest   C#: ApprovalPayloadEnvelopeConformanceTests
The source checks below are keyword checks that keep the two implementations' wire constants in step; they are not
evidence of behaviour.
"""
import base64, json, re, subprocess, sys
from pathlib import Path

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

ROOT = Path(__file__).resolve().parents[5]
CONF = ROOT / "src/conformance"
VECTORS = CONF / "approval-envelope-vectors.json"
GENERATOR = CONF / "tools/generate_approval_envelope_vectors.py"
JAVA = ROOT / "src/java/foundgine-providers/src/main/java/com/foundgine/providers/gateway/approvals/ApprovalPayloadEnvelope.java"
CS = ROOT / "src/csharp/Foundgine.Providers/Gateway/Approvals/ApprovalPayloadEnvelope.cs"
JAVA_TEST = ROOT / "src/java/foundgine-providers/src/test/java/com/foundgine/providers/gateway/approvals/ApprovalPayloadEnvelopeConformanceTest.java"
CS_TEST = ROOT / "src/csharp/tests/Foundgine.Gateway.Tests/ApprovalPayloadEnvelopeConformanceTests.cs"

SHAPE = re.compile(r'\A\{"v":1,"kid":"([A-Za-z0-9._-]{1,64})","n":"([A-Za-z0-9+/]{16})","c":"([A-Za-z0-9+/]+={0,2})"\}\Z')


def load():
    return json.loads(VECTORS.read_text(encoding="utf-8"))


def test_vectors_are_exactly_what_the_reference_generator_produces():
    out = subprocess.run([sys.executable, str(GENERATOR)], check=True, capture_output=True, text=True, encoding="utf-8").stdout
    assert out == VECTORS.read_text(encoding="utf-8")


def test_every_seal_vector_decrypts_with_an_independent_implementation():
    d = load()
    for c in d["sealCases"]:
        m = SHAPE.match(c["envelope"])
        assert m, c["name"]
        key = bytes.fromhex(d["keys"][m.group(1)])
        pt = AESGCM(key).decrypt(base64.b64decode(m.group(2)), base64.b64decode(m.group(3)), f"foundgine.approval.v1|{m.group(1)}".encode())
        assert pt.decode("utf-8") == c["plaintext"], c["name"]


def test_reject_vectors_fail_either_structurally_or_cryptographically():
    d = load()
    for c in d["rejectCases"]:
        m = SHAPE.match(c["envelope"])
        if not m:
            continue  # structural rejection
        kid = m.group(1)
        if kid not in d["keys"]:
            continue  # unknown key rejection
        try:
            AESGCM(bytes.fromhex(d["keys"][kid])).decrypt(base64.b64decode(m.group(2)), base64.b64decode(m.group(3)), f"foundgine.approval.v1|{kid}".encode())
        except InvalidTag:
            continue
        raise AssertionError(f"{c['name']} was accepted")


def test_vector_set_covers_the_required_failure_classes():
    names = {c["name"] for c in load()["rejectCases"]}
    assert {"tampered-ciphertext", "relabelled-key-id", "unknown-key-id", "version-2", "trailing-whitespace",
            "extra-field", "short-nonce", "empty-ciphertext"} <= names


def test_runtimes_share_wire_constants():
    j, c = JAVA.read_text(), CS.read_text()
    for text in (j, c):
        assert "foundgine.approval.v1|" in text
        assert "[A-Za-z0-9._-]{1,64}" in text
        assert '[A-Za-z0-9+/]{16}' in text
    assert "GCMParameterSpec(TAG_BITS" in j and "TAG_BITS = 128" in j
    assert "AesGcm(key, TagBytes)" in c and "TagBytes = 16" in c
    assert r"\A" in c and r"\z" in c, "C# must anchor with \\A..\\z so a trailing newline is not accepted"


def test_both_runtimes_assert_the_same_vector_file_and_test_set():
    assert "approval-envelope-vectors.json" in JAVA_TEST.read_text()
    assert "approval-envelope-vectors.json" in CS_TEST.read_text()
    jn = {n.replace("_", "").lower() for n in re.findall(r"void\s+(\w+)\(", JAVA_TEST.read_text())}
    cn = {n.replace("_", "").lower() for n in re.findall(r"public void (\w+)\(", CS_TEST.read_text())}
    assert jn == cn, (jn ^ cn)
