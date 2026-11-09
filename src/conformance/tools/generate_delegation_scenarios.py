#!/usr/bin/env python3
"""Delegation scenarios for cross-language conformance (see ../README.md).

Regenerate with:  python3 tools/generate_delegation_scenarios.py > delegation-scenarios.json

Each scenario names an operation, a set of warrants and the outcome both runtimes must produce: `error` is the exact
exception message (identical in C# and Java) or null for success. In `parentDigest` and `delegationPath`, the token
"$digest:N" is replaced by the canonical digest of warrant N (N must precede the warrant using it).
"""
import hashlib, json, sys
from generate_warrant_vectors import base, canonical

T0 = '2026-11-09T07:00:00.0000000Z'
NOW = '2026-11-09T07:30:00.0000000Z'
T_END = '2026-11-09T08:00:00.0000000Z'
HEX0 = '00' * 32

def resolve(ws):
    digests, out = [], []
    for w in ws:
        w = json.loads(json.dumps(w))
        sub = lambda x: digests[int(x.split(':')[1])] if isinstance(x, str) and x.startswith('$digest:') else x
        w['parentDigest'] = sub(w.get('parentDigest'))
        w['delegationPath'] = [sub(x) for x in w.get('delegationPath') or []]
        digests.append(hashlib.sha256(canonical(w).encode('utf-8')).hexdigest().upper())
        out.append(w)
    return out, digests

def chain_digest(digests):
    h = hashlib.sha256()
    for d in digests:
        b = bytes.fromhex(d); h.update(len(b).to_bytes(4, 'big')); h.update(b)
    return h.hexdigest().upper()

GRANT = dict(capability='orders', operation='read', resourceScopes=['a', 'b'])
def parent(**o):
    w = dict(id='p', subject='mid', issuedAt=T0, expiresAt=T_END, grants=[GRANT],
             constraints=dict(allowedTenants=['t1', 't2'], resourceScopes=['a', 'b'], allowedOperations=['read'],
                              maxResults=10, maxAmount='10.0'))
    w.update(o); return base(**w)
def child(**o):
    w = dict(id='c', issuer='mid', subject='leaf', issuedAt=T0, expiresAt=T_END, grants=[dict(GRANT, resourceScopes=['a'])],
             constraints=dict(allowedTenants=['t1'], resourceScopes=['a'], allowedOperations=['read'], maxResults=5,
                              maxAmount='10.00'),
             parentId='p', parentDigest='$digest:0', delegationPath=['$digest:0'])
    w.update(o); return base(**w)

SCENARIOS = []
def att(name, p, c, error, now=NOW):
    SCENARIOS.append(dict(name=name, op='attenuate', now=now, warrants=[p, c], error=error))

att('valid narrowing', parent(), child(constraints=dict(allowedTenants=['t1'], resourceScopes=['a'], allowedOperations=['read'], maxResults=5, maxAmount='5')), None)
att('numerically equal decimals with different scale are accepted', parent(), child(), None)
att('parent expired', parent(), child(), 'Cannot attenuate an expired or not-yet-valid parent warrant.', now='2026-11-09T09:00:00.0000000Z')
att('wrong parent id', parent(), child(parentId='x'), 'Child warrant must identify its parent warrant.')
att('missing parent id', parent(), child(parentId=None), 'Child warrant must identify its parent warrant.')
att('wrong parent digest', parent(), child(parentDigest=HEX0), 'Child warrant must bind cryptographically to its parent digest.')
att('issuer is not parent subject', parent(), child(issuer='someone-else'), 'Delegated issuer must be the parent subject.')
att('audience broadened', parent(), child(audience='other'), 'Delegation cannot broaden the audience.')
att('child predates parent', parent(), child(issuedAt='2026-11-09T06:59:59.9999999Z'), 'Child warrant cannot predate parent issuance.')
att('child extends expiry by one tick', parent(), child(expiresAt='2026-11-09T08:00:00.0000001Z'), 'Child warrant cannot extend parent expiry.')
att('depth skips a level', parent(), child(delegationPath=['$digest:0', HEX0]), 'Delegation depth must increase exactly one level.')
att('path does not end in parent digest', parent(), child(delegationPath=[HEX0]), 'Child delegation path does not match the parent chain.')
att('child adds a capability', parent(), child(grants=[dict(capability='orders', operation='write', resourceScopes=['a'])]), "Child warrant adds capability 'orders'.")
att('child broadens grant scope', parent(), child(grants=[dict(GRANT, resourceScopes=['a', 'z'])]), "Child warrant broadens resource scope for 'orders'.")
att('unrestricted parent grant scope allows any child scope', parent(grants=[dict(GRANT, resourceScopes=[])]), child(grants=[dict(GRANT, resourceScopes=['z'])]), None)
att('child drops max results', parent(), child(constraints=dict(allowedTenants=['t1'], resourceScopes=['a'], allowedOperations=['read'], maxAmount='1')), 'Child warrant constraints broaden parent authority.')
att('child raises max amount', parent(), child(constraints=dict(allowedTenants=['t1'], resourceScopes=['a'], allowedOperations=['read'], maxResults=5, maxAmount='10.01')), 'Child warrant constraints broaden parent authority.')
att('child empty tenants under restricted parent', parent(), child(constraints=dict(resourceScopes=['a'], allowedOperations=['read'], maxResults=5, maxAmount='1')), 'Child warrant constraints broaden parent authority.')
deep = ['%064X' % (i + 1) for i in range(32)]
att('depth 33 exceeds maximum', parent(delegationPath=deep), child(delegationPath=deep + ['$digest:0']), 'Maximum delegation depth exceeded.')

def chain(name, ws, error, now=NOW, expected_root=None, check_digest=False):
    s = dict(name=name, op='validateChain', now=now, warrants=ws, error=error)
    if expected_root is not None: s['expectedRootDigest'] = expected_root
    SCENARIOS.append(s)

def root(): return base(id='r', subject='alice', issuedAt=T0, expiresAt=T_END)
def mid(**o):
    w = dict(id='m', issuer='alice', subject='bob', issuedAt=T0, expiresAt=T_END, parentId='r', parentDigest='$digest:0', delegationPath=['$digest:0'])
    w.update(o); return base(**w)
def leaf(**o):
    w = dict(id='l', issuer='bob', subject='carol', issuedAt=T0, expiresAt=T_END, parentId='m', parentDigest='$digest:1', delegationPath=['$digest:0', '$digest:1'])
    w.update(o); return base(**w)

chain('valid three-warrant chain', [root(), mid(), leaf()], None)
chain('empty chain', [], 'A delegation chain must contain at least one warrant.')
chain('chain longer than depth limit', [root()] + [mid()] * 33, 'Delegation chain exceeds the maximum supported depth.')
chain('root carries ancestry', [base(id='r', subject='alice', issuedAt=T0, expiresAt=T_END, parentId='x')], 'The root warrant cannot contain delegation ancestry.')
chain('root expired', [root()], 'The root warrant is not currently valid.', now='2026-11-09T09:00:00.0000000Z')
chain('root digest mismatch', [root()], 'Delegation chain root does not match the expected root digest.', expected_root=HEX0)
chain('ancestor expired', [root(), mid(expiresAt='2026-11-09T07:20:00.0000000Z')], 'A delegated warrant is not currently valid.')
chain('parent id substituted', [root(), mid(parentId='zzz')], 'Delegation chain contains a parent-id substitution or reorder.')
chain('parent digest substituted', [root(), mid(parentDigest=HEX0)], 'Delegation chain contains a parent-digest substitution.')
chain('issuer is not previous subject', [root(), mid(issuer='mallory')], 'Delegation chain issuer does not match the parent subject.')
chain('path length wrong', [root(), mid(delegationPath=['$digest:0', HEX0])], 'Delegation depth/path length does not match the supplied chain position.')
chain('path spliced', [root(), mid(), leaf(delegationPath=[HEX0, '$digest:1'])], 'Delegation path contains a splice, reorder, or substituted ancestor.')
chain('repeated warrant id', [root(), mid(id='r')], 'Delegation chain contains a repeated warrant id.')

def main():
    out = []
    for s in SCENARIOS:
        ws, digests = resolve(s['warrants'])
        s = dict(s, warrants=ws, digests=digests)
        if s['op'] == 'validateChain' and s['error'] is None:
            s['chainDigest'] = chain_digest(digests)
        out.append(s)
    json.dump(dict(spec='warrant-delegation-scenarios-v1', scenarios=out), sys.stdout, indent=1, ensure_ascii=True)
    print()

if __name__ == '__main__':
    main()
