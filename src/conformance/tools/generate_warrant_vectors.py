#!/usr/bin/env python3
"""Reference implementation of the Foundgine warrant canonical form v1 (see ../README.md).

Regenerate with:  python3 tools/generate_warrant_vectors.py > warrant-canonicalization.json
C# and Java must reproduce every vector's `unsignedJson` and `digest` byte for byte.
"""
import hashlib, json, re, sys
from datetime import datetime, timedelta

def u16(s): return s.encode("utf-16-be", "surrogatepass")  # UTF-16 code-unit order == .NET Ordinal == Java compareTo

def norm(values):
    return sorted({v for v in (values or []) if v.strip(' \t\n\v\f\r')}, key=u16)

def esc(v):
    out = ['"']
    for c in v:
        o = ord(c)
        if c == '"': out.append('\\"')
        elif c == '\\': out.append('\\\\')
        elif c == '\n': out.append('\\n')
        elif c == '\r': out.append('\\r')
        elif c == '\t': out.append('\\t')
        elif c == '\b': out.append('\\b')
        elif c == '\f': out.append('\\f')
        elif o < 0x20: out.append('\\u%04x' % o)
        else: out.append(c)
    out.append('"')
    return ''.join(out)

def arr(vs): return '[' + ','.join(esc(v) for v in vs) + ']'
def opt(v): return 'null' if v is None else esc(v)

def utc(s):
    m = re.fullmatch(r'(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.(\d{1,7}))?(Z|[+-]\d\d:\d\d)', s)
    assert m, s
    base = datetime.strptime(m.group(1), '%Y-%m-%dT%H:%M:%S')
    frac = (m.group(2) or '').ljust(7, '0')
    if m.group(3) != 'Z':
        sign = 1 if m.group(3)[0] == '+' else -1
        base -= sign * timedelta(hours=int(m.group(3)[1:3]), minutes=int(m.group(3)[4:6]))
    return base.strftime('%Y-%m-%dT%H:%M:%S') + '.' + frac + 'Z'

def canonical(w):
    grants = [{**g, 'resourceScopes': norm(g.get('resourceScopes'))} for g in w['grants']]
    grants.sort(key=lambda g: (u16(g['capability']), u16(g['operation']), u16('\x1f'.join(g['resourceScopes']))))
    c = w['constraints']
    gs = ','.join('{"capability":%s,"operation":%s,"resourceScopes":%s}' % (esc(g['capability']), esc(g['operation']), arr(g['resourceScopes'])) for g in grants)
    mr = 'null' if c.get('maxResults') is None else str(c['maxResults'])
    ma = 'null' if c.get('maxAmount') is None else c['maxAmount']
    cs = '{"allowedTenants":%s,"allowedFields":%s,"resourceScopes":%s,"allowedOperations":%s,"maxResults":%s,"maxAmount":%s}' % (
        arr(norm(c.get('allowedTenants'))), arr(norm(c.get('allowedFields'))), arr(norm(c.get('resourceScopes'))),
        arr(norm(c.get('allowedOperations'))), mr, ma)
    return ('{"id":%s,"issuer":%s,"subject":%s,"audience":%s,"grants":[%s],"constraints":%s,"issuedAt":%s,"expiresAt":%s,'
            '"nonce":%s,"keyId":%s,"parentId":%s,"parentDigest":%s,"delegationPath":%s}') % (
        esc(w['id']), esc(w['issuer']), esc(w['subject']), esc(w['audience']), gs, cs,
        esc(utc(w['issuedAt'])), esc(utc(w['expiresAt'])), esc(w['nonce']), esc(w['keyId']),
        opt(w.get('parentId')), opt(w.get('parentDigest')), arr(w.get('delegationPath') or []))

def base(**over):
    w = dict(id='w-1', issuer='foundgine-keycloak-bridge', subject='alice', audience='foundgine', grants=[],
             constraints={}, issuedAt='2026-11-09T07:25:00.0000000Z', expiresAt='2026-11-09T07:30:00.0000000Z',
             nonce='n-1', keyId='k-1', parentId=None, parentDigest=None, delegationPath=[])
    w.update(over); return w

VECTORS = [
 ('minimal ascii', base()),
 ('plus sign in email subject', base(subject='alice+test@example.com')),
 ('html-sensitive characters are not escaped', base(subject="a&b<c>d'e`f")),
 ('non-ascii bmp is emitted raw', base(subject='José Müller 東京')),
 ('astral character (surrogate pair) is emitted raw', base(subject='ok 😀 end')),
 ('quote, backslash and control characters', base(subject='q"b\\n\nr\rt\tb\bf\fv\u000bz\u001f del\u007f')),
 ('grants sorted and scopes normalized', base(grants=[
     dict(capability='orders', operation='write', resourceScopes=['b', 'a', 'a', ' ']),
     dict(capability='orders', operation='read', resourceScopes=['z']),
     dict(capability='accounts', operation='read', resourceScopes=[])])),
 ('utf-16 code unit ordering (U+FF5E sorts after U+10000)', base(grants=[
     dict(capability='c', operation='o', resourceScopes=['\uff5e', '\U00010000', 'a'])])),
 ('constraints with limits and decimal scale preserved', base(constraints=dict(
     allowedTenants=['t2', 't1'], allowedFields=['name'], resourceScopes=['r'], allowedOperations=['read', 'write'],
     maxResults=9007199254740993, maxAmount='100.50'))),
 ('decimal with trailing zeros', base(constraints=dict(maxAmount='0.0010'))),
 ('delegated warrant', base(parentId='w-0', parentDigest='AB' * 32, delegationPath=['AA' * 32, 'BB' * 32])),
 ('offset input normalizes to utc', base(issuedAt='2026-11-09T09:25:00.1234567+02:00', expiresAt='2026-11-09T07:30:00Z')),
 ('blank means ascii whitespace only; other unicode spaces are kept', base(grants=[
     dict(capability='c', operation='o', resourceScopes=[' ', '\t', '\u000b', '\u00a0', '\u2003', '\u0085', '\u001c', 'x'])])),
 ('whole-second timestamps keep seven fractional digits', base(issuedAt='2026-01-02T03:04:05Z', expiresAt='2026-01-02T03:09:05Z')),
]

def main():
    out = []
    for name, w in VECTORS:
        j = canonical(w)
        out.append(dict(name=name, warrant=w, unsignedJson=j, digest=hashlib.sha256(j.encode('utf-8')).hexdigest().upper()))
    json.dump(dict(spec='warrant-canonical-form-v1', vectors=out), sys.stdout, indent=1, ensure_ascii=True)
    print()

if __name__ == '__main__':
    main()
