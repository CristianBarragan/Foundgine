import asyncio, json, os, sys, threading, unittest, urllib.parse
from http.server import BaseHTTPRequestHandler, HTTPServer

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from fpbench.auth import TokenCache, identities
from fpbench.loadgen import Target


class _KC(BaseHTTPRequestHandler):
    issued = 0
    bad_users = set()

    def do_POST(self):
        form = urllib.parse.parse_qs(self.rfile.read(int(self.headers["Content-Length"])).decode())
        user = form["username"][0]
        if user in _KC.bad_users or form["grant_type"] != ["password"]:
            self.send_response(401); self.end_headers(); return
        _KC.issued += 1
        body = json.dumps({"access_token": f"tok-{user}-{_KC.issued}", "expires_in": 1}).encode()
        self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

    def log_message(self, *a):
        pass


class AuthTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.srv = HTTPServer(("127.0.0.1", 0), _KC)
        threading.Thread(target=cls.srv.serve_forever, daemon=True).start()
        cls.url = f"http://127.0.0.1:{cls.srv.server_port}"

    @classmethod
    def tearDownClass(cls):
        cls.srv.shutdown(); cls.srv.server_close()

    def cache(self, **kw):
        return TokenCache(self.url, "supplychain", "supplychain-bench", "pw", margin_s=kw.pop("margin_s", 0.6), **kw)

    def test_off_when_no_block(self):
        self.assertIsNone(TokenCache.from_cfg(None))

    def test_prefetch_and_header(self):
        tc = self.cache(); tc.prefetch(["alice", "bob", "alice"])
        self.assertEqual(tc.fetches, 2)
        sc = {"name": "t", "requests": [{"method": "POST", "path": "", "body": {}, "as": "alice"}]}
        _, _, _, h = Target("http://127.0.0.1:1/graphql", sc, tokens=tc).build(0)
        self.assertTrue(h["Authorization"].startswith("Bearer tok-alice-"))
        self.assertEqual(identities(sc), {"alice"})

    def test_missing_auth_block_is_an_error_not_a_silent_anonymous_request(self):
        sc = {"name": "t", "requests": [{"path": "", "as": "alice"}]}
        with self.assertRaises(RuntimeError):
            Target("http://127.0.0.1:1/x", sc).build(0)

    def test_unknown_identity(self):
        tc = self.cache(); tc.prefetch(["alice"])
        with self.assertRaises(KeyError):
            tc.get("mallory")

    def test_refresh_before_expiry(self):
        tc = self.cache(); tc.prefetch(["alice"]); first = tc.get("alice")

        async def run():
            stop = asyncio.Event()
            task = asyncio.create_task(tc.keep_fresh(stop, interval=0.1))
            await asyncio.sleep(1.5)
            stop.set(); await task
        asyncio.run(run())
        self.assertNotEqual(tc.get("alice"), first)

    def test_failed_refresh_is_counted(self):
        tc = self.cache(); tc.prefetch(["carol"]); _KC.bad_users.add("carol")
        try:
            async def run():
                stop = asyncio.Event()
                task = asyncio.create_task(tc.keep_fresh(stop, interval=0.1))
                await asyncio.sleep(1.0); stop.set(); await task
            asyncio.run(run())
        finally:
            _KC.bad_users.discard("carol")
        self.assertGreater(tc.failures, 0)


if __name__ == "__main__":
    unittest.main()
