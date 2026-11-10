#!/usr/bin/env python3
"""Reference implementation of the Foundgine approval payload envelope v1 (AES-256-GCM).

Regenerate with:  python3 tools/generate_approval_envelope_vectors.py > approval-envelope-vectors.json
C# and Java must (a) reproduce every `sealCases[].envelope` byte for byte from the fixed nonce, (b) open it back to
`plaintext`, and (c) reject every `rejectCases[].envelope`. Fixed nonces exist ONLY for these vectors.

Envelope: {"v":1,"kid":"<id>","n":"<b64 12-byte nonce>","c":"<b64 ciphertext||16-byte tag>"}
AAD:      UTF-8("foundgine.approval.v1|" + kid)
"""
import base64, json
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

KEYS = {"k1": bytes(range(32)), "k2": bytes(range(32, 64))}

def b64(b): return base64.b64encode(b).decode()

def seal(plaintext, kid, nonce):
    ct = AESGCM(KEYS[kid]).encrypt(nonce, plaintext.encode("utf-8"), ("foundgine.approval.v1|" + kid).encode())
    return '{"v":1,"kid":"%s","n":"%s","c":"%s"}' % (kid, b64(nonce), b64(ct))

seal_cases = [
    ("ascii-json", "k1", bytes(range(12)), '{"plan":"abc","tenant":null}'),
    ("unicode-and-escapes", "k2", bytes(range(100, 112)), '{"note":"caf\u00e9 \u2603 \U0001F512","q":"a\\"b\\\\c"}'),
    ("empty-plaintext", "k1", bytes([7] * 12), ""),
]

base = seal("{}", "k1", bytes(range(12)))
def tamper_ct(env):
    obj = json.loads(env); c = bytearray(base64.b64decode(obj["c"])); c[0] ^= 1
    obj["c"] = b64(bytes(c)); return json.dumps(obj, separators=(",", ":"))

reject_cases = [
    ("tampered-ciphertext", tamper_ct(base)),
    ("relabelled-key-id", base.replace('"kid":"k1"', '"kid":"k2"')),
    ("unknown-key-id", seal("{}", "k1", bytes(range(12))).replace('"kid":"k1"', '"kid":"k9"')),
    ("version-2", base.replace('"v":1', '"v":2')),
    ("whitespace-in-envelope", base.replace('{"v":1,', '{ "v":1,')),
    ("trailing-whitespace", base + " "),
    ("extra-field", base[:-1] + ',"x":1}'),
    ("field-order-swapped", '{"kid":"k1","v":1,' + base.split('"kid":"k1",', 1)[1]),
    ("short-nonce", base.replace(json.loads(base)["n"], b64(bytes(range(8))))),
    ("empty-ciphertext", base.replace(json.loads(base)["c"], "")),
    ("not-json", "plain text"),
    ("raw-plaintext-json", '{"plan":"abc"}'),
]

print(json.dumps({
    "description": "Approval payload envelope v1 cross-runtime vectors. Keys are hex; AAD = 'foundgine.approval.v1|<kid>'.",
    "keys": {k: v.hex() for k, v in KEYS.items() if k != "k9"},
    "currentKeyId": "k1",
    "sealCases": [{"name": n, "keyId": k, "nonceHex": nonce.hex(), "plaintext": p, "envelope": seal(p, k, nonce)}
                  for n, k, nonce, p in seal_cases],
    "rejectCases": [{"name": n, "envelope": e} for n, e in reject_cases],
}, indent=2, ensure_ascii=False))
