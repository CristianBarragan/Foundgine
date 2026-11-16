"""Tiny keep-alive HTTP mock: python mock_server.py PORT DELAY_MS [variant]"""
import asyncio, json, sys
port, delay, variant = int(sys.argv[1]), float(sys.argv[2]) / 1000, (sys.argv[3] if len(sys.argv) > 3 else "ok")
async def handle(r, w):
    try:
        while True:
            line = await r.readline()
            if not line: break
            clen = 0
            while True:
                h = await r.readline()
                if h in (b"\r\n", b""): break
                if h.lower().startswith(b"content-length"): clen = int(h.split(b":")[1])
            if clen: await r.readexactly(clen)
            await asyncio.sleep(delay)
            body = json.dumps({"data": {"customers": [{"id": i, "name": f"c{i}" if variant == "ok" else f"X{i}"} for i in range(5)]}}).encode()
            w.write(b"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: %d\r\n\r\n" % len(body) + body)
            await w.drain()
    except ConnectionError: pass
    finally: w.close()
async def main():
    s = await asyncio.start_server(handle, "127.0.0.1", port)
    async with s: await s.serve_forever()
asyncio.run(main())
