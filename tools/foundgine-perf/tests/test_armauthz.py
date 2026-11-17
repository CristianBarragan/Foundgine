import json, os, sys, tempfile, threading, unittest, base64
from http.server import BaseHTTPRequestHandler, HTTPServer
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from fpbench import armauthz as az

def jwt(user):  # unsigned stand-in; the fake arms below only inspect the payload
    b = lambda d: base64.urlsafe_b64encode(json.dumps(d).encode()).rstrip(b"=").decode()
    return f"{b({'alg':'RS256'})}.{b({'preferred_username': user})}.sig"

class Arm(BaseHTTPRequestHandler):
    leak = False   # a broken arm that ignores the tenant rule
    def do_POST(self):
        n = int(self.headers.get("Content-Length", 0)); self.rfile.read(n)
        auth = self.headers.get("Authorization", "")
        user = None
        try:
            user = json.loads(base64.urlsafe_b64decode(auth.split(".")[1] + "==")).get("preferred_username") if auth.count(".") == 2 else None
        except Exception: pass
        if not user or auth.endswith("sig-bad"): self.send_response(401); self.end_headers(); return
        rows = [{"id": 1, "tenantId": "tenant-a"}] + ([{"id": 3, "tenantId": "tenant-b"}] if (self.leak or user == "erin") else [])
        body = json.dumps({"data": {"supplier": rows}}).encode()
        self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *a): pass

class Tests(unittest.TestCase):
    def test_decide(self):
        self.assertEqual(az.decide(401, b""), "unauthenticated")
        self.assertEqual(az.decide(200, b'{"errors":[{"message":"x"}]}'), "deny")
        self.assertEqual(az.decide(200, b'{"data":{"updateWarehouse":null}}'), "deny")   # write that matched nothing
        self.assertEqual(az.decide(200, b'{"data":{"supplier":[]}}'), "allow")           # empty read is allowed
        self.assertEqual(az.decide(500, b""), "error")

    def test_rows_rule_catches_tenant_leak(self):
        ok, _ = az.rows_ok(b'{"data":{"supplier":[{"tenantId":"tenant-a"}]}}', {"path": "supplier", "field": "tenantId", "in": ["tenant-a"], "min_rows": 1})
        self.assertTrue(ok)
        ok, d = az.rows_ok(b'{"data":{"supplier":[{"tenantId":"tenant-a"},{"tenantId":"tenant-b"}]}}', {"path": "supplier", "field": "tenantId", "in": ["tenant-a"]})
        self.assertFalse(ok); self.assertIn("tenant-b", d)

if __name__ == "__main__":
    unittest.main()
