#!/usr/bin/env python3
"""Signature interoperability vectors (see ../README.md).

Regenerate with:  python3 tools/generate_signature_vectors.py > warrant-signatures.json
RSA PKCS#1 v1.5 with SHA-256 is deterministic, so every implementation must (a) reproduce `signature` exactly when
signing the warrant with `privateKeyPkcs8`, and (b) accept/reject each case with exactly `error`.
The key pair is TEST-ONLY and public on purpose; never reuse it. It is generated once (keys.json next to this script is
not needed): set FOUNDGINE_REGENERATE_KEYS=1 to mint new keys, otherwise the existing JSON file's keys are reused.
"""
import base64, json, os, sys
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from generate_warrant_vectors import base, canonical, VECTORS

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'warrant-signatures.json')

def load_or_make(name):
    if os.path.exists(OUT) and not os.environ.get('FOUNDGINE_REGENERATE_KEYS'):
        k = json.load(open(OUT))['keys'][name]
        return serialization.load_der_private_key(base64.b64decode(k['privateKeyPkcs8']), None)
    return rsa.generate_private_key(65537, 2048)

def der(key):
    return dict(
        privateKeyPkcs8=base64.b64encode(key.private_bytes(serialization.Encoding.DER, serialization.PrivateFormat.PKCS8, serialization.NoEncryption())).decode(),
        publicKeySpki=base64.b64encode(key.public_key().public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)).decode())

KEYS = {'k-1': load_or_make('k-1'), 'other': load_or_make('other')}

def sign(w, key='k-1'):
    return base64.b64encode(KEYS[key].sign(canonical(w).encode('utf-8'), padding.PKCS1v15(), hashes.SHA256())).decode()

NOW = '2026-11-09T07:27:00.0000000Z'
NAMED = {n: w for n, w in VECTORS}
CASES = []
def case(name, w, signature, error, now=NOW, issuer='foundgine-keycloak-bridge', audience='foundgine', keys=('k-1',), sign_check=False):
    CASES.append(dict(name=name, warrant=w, signature=signature, now=now, expectedIssuer=issuer, expectedAudience=audience,
                      resolvableKeyIds=list(keys), error=error, mustReproduceSignature=sign_check))

for n in ['minimal ascii', 'plus sign in email subject', 'non-ascii bmp is emitted raw',
          'astral character (surrogate pair) is emitted raw', 'quote, backslash and control characters',
          'constraints with limits and decimal scale preserved', 'delegated warrant',
          'blank means ascii whitespace only; other unicode spaces are kept']:
    w = NAMED[n]
    case('valid: ' + n, w, sign(w), None, sign_check=True)

w = base()
case('tampered subject', base(subject='mallory'), sign(w), 'Security warrant signature is invalid.')
case('tampered scope', base(grants=[dict(capability='c', operation='o', resourceScopes=['x'])]), sign(base(grants=[dict(capability='c', operation='o', resourceScopes=['y'])])), 'Security warrant signature is invalid.')
case('signed by a different key', w, sign(w, 'other'), 'Security warrant signature is invalid.')
case('no signature', w, '', 'Security warrant has no signature.')
case('expired', w, sign(w), 'Security warrant is expired or not yet valid.', now='2026-11-09T07:31:00.0000000Z')
case('not yet valid', w, sign(w), 'Security warrant is expired or not yet valid.', now='2026-11-09T07:00:00.0000000Z')
inv = base(expiresAt='2026-11-09T07:25:00.0000000Z')
case('expiry not after issued-at', inv, sign(inv), 'Security warrant expiry must be after issued-at.')
case('untrusted issuer', w, sign(w), 'Security warrant issuer is not trusted.', issuer='someone-else')
case('untrusted audience', w, sign(w), 'Security warrant audience is not trusted.', audience='other')
case('no expected issuer configured', w, sign(w), 'Security warrant verification requires a configured expected issuer.', issuer=None)
k2 = base(keyId='k-2')
case('unknown key id', k2, sign(k2), "Unknown warrant key 'k-2'.")

json.dump(dict(spec='warrant-signatures-v1', keys={k: der(v) for k, v in KEYS.items()}, cases=CASES), sys.stdout, indent=1, ensure_ascii=True)
print()
