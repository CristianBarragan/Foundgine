#!/usr/bin/env python3
"""Mint an HS256 JWT for both apps. usage: mint_token.py <role> [customer_id] [--no-tenant] [--bad-sig] [--expired]"""
import base64, hmac, hashlib, json, os, sys, time
def b64(b): return base64.urlsafe_b64encode(b).rstrip(b"=").decode()
a = [x for x in sys.argv[1:] if not x.startswith("--")]; flags = {x for x in sys.argv[1:] if x.startswith("--")}
role = a[0]
now = int(time.time())
claims = {"iss": "https://auth.local", "aud": "shop-api", "sub": f"user-{role}", "role": role,
          "iat": now, "exp": now - 60 if "--expired" in flags else now + 3600}
if "--no-tenant" not in flags: claims["tenant_id"] = "acme"
if len(a) > 1: claims["customer_id"] = a[1]
key = os.environ.get("JWT_SIGNING_KEY", "dev-only-signing-key-change-me-32bytes!!")
if "--bad-sig" in flags: key = "some-other-key-some-other-key-123456"
h = b64(json.dumps({"alg": "HS256", "typ": "JWT"}).encode()); p = b64(json.dumps(claims).encode())
s = b64(hmac.new(key.encode(), f"{h}.{p}".encode(), hashlib.sha256).digest())
print(f"{h}.{p}.{s}")
