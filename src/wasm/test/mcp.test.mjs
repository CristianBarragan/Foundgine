import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { FoundgineClient, FoundgineAbortError, FoundgineHttpError, FoundgineRpcError, FoundgineSessionExpiredError, FoundgineResyncRequiredError, init } from "../js/index.js";
import { startServer } from "./server.mjs";

await init();

let server;
before(async () => {
  server = await startServer();
});
after(() => server.close());

const reset = () => {
  server.state.requests.length = 0;
  server.state.streamRuns = 0;
  server.state.streamStatus = undefined;
  server.state.keepOpen = false;
};
const client = (extra = {}) => new FoundgineClient({ baseUrl: server.baseUrl, ...extra });
const posts = () => server.state.requests.filter((r) => r.method === "POST").map((r) => r.body.method);

test("base URL must be http(s)", () => {
  assert.throws(() => new FoundgineClient({ baseUrl: "ftp://x" }), /baseUrl must start with http/);
});

test("concurrent first calls share one initialize; later calls carry session and protocol headers", async () => {
  reset();
  const c = client({ token: "secret" });
  const [a, b] = await Promise.all([c.callTool("echo", { n: 1 }), c.callTool("echo", { n: 2 })]);
  assert.deepEqual(a.structuredContent, { echoed: { n: 1 } });
  assert.deepEqual(b.structuredContent, { echoed: { n: 2 } });
  assert.equal(posts().filter((m) => m === "initialize").length, 1);
  assert.deepEqual(posts().slice(0, 2), ["initialize", "notifications/initialized"]);
  const call = server.state.requests.find((r) => r.body?.method === "tools/call");
  assert.match(call.headers["mcp-session-id"], /^sess-/);
  assert.equal(call.headers["mcp-protocol-version"], "2025-06-18");
  assert.equal(call.headers.authorization, "Bearer secret");
  assert.equal(server.state.requests[0].headers["mcp-session-id"], undefined, "initialize carries no session");
  assert.equal(c.mcp.sessionId, call.headers["mcp-session-id"]);
  await c.close();
});

test("token function is resolved per request; blank means no Authorization header", async () => {
  reset();
  let n = 0;
  const c = client({ token: async () => (++n === 1 ? "" : `t${n}`) });
  await c.callTool("echo", {});
  const auth = server.state.requests.map((r) => r.headers.authorization);
  assert.equal(auth[0], undefined, "first resolution was blank");
  assert.ok(auth.slice(1).some((a) => a?.startsWith("Bearer t")));
  assert.equal(n, 3, "initialize, initialized and the call each resolve the token");
  await c.close();
});

test("canonical client aliases match the TypeScript and Python public surface", async () => {
  reset();
  const c = client();
  const tools = await c.listCapabilities();
  assert.deepEqual(tools.map((t) => t.name), ["echo", "add"]);
  const result = await c.execute("echo", { value: 7 });
  assert.equal(result.structuredContent.echoed.value, 7);
  assert.equal(typeof c.notifications, "function");
  await c.close();
});

test("listTools follows nextCursor and skips entries without a name", async () => {
  reset();
  const c = client();
  const tools = await c.listTools();
  assert.deepEqual(tools.map((t) => t.name), ["echo", "add"]);
  assert.equal(tools[0].description, "Echo");
  const lists = server.state.requests.filter((r) => r.body?.method === "tools/list");
  assert.deepEqual(lists.map((r) => r.body.params), [{}, { cursor: "p2" }]);
  await c.close();
});

test("a failing tool is data; an RPC error and an HTTP error are thrown", async () => {
  reset();
  const c = client();
  const failed = await c.callTool("boom", {});
  assert.equal(failed.isError, true);
  assert.equal(failed.content[0].text, "failed");
  assert.equal("structuredContent" in failed, false, "structuredContent only when the server sent it");
  await assert.rejects(c.mcp.request("does/not/exist"), (e) => e instanceof FoundgineRpcError && e.code === -32601 && /JSON-RPC error -32601: Method not found/.test(e.message) && e.data.method === "does/not/exist");
  await c.close();
});

test("404 expires the session; the next call initializes again", async () => {
  reset();
  const c = client();
  await c.callTool("echo", {});
  const first = c.mcp.sessionId;
  server.state.expireNext = true;
  await assert.rejects(c.callTool("echo", {}), (e) => e instanceof FoundgineSessionExpiredError && e instanceof FoundgineHttpError && e.status === 404);
  assert.equal(c.mcp.isReady, false);
  await c.callTool("echo", {});
  assert.notEqual(c.mcp.sessionId, first);
  assert.equal(posts().filter((m) => m === "initialize").length, 2);
  await c.close();
});

test("abort sends notifications/cancelled and throws FoundgineAbortError", async () => {
  reset();
  const c = client();
  const ctl = new AbortController();
  const pending = c.callTool("slow", {}, { signal: ctl.signal });
  setTimeout(() => ctl.abort(), 50);
  await assert.rejects(pending, FoundgineAbortError);
  await new Promise((r) => setTimeout(r, 50));
  const cancelled = server.state.requests.find((r) => r.body?.method === "notifications/cancelled");
  assert.ok(cancelled, "cancel notification reached the server");
  assert.equal(cancelled.body.params.reason, "cancelled by client");
  await assert.rejects(c.callTool("echo", {}, { signal: ctl.signal }), FoundgineAbortError, "an already-aborted signal fails fast");
  await c.close();
});

test("notifications: decodes split multi-byte chunks, reconnects with Last-Event-ID after the server's retry", async () => {
  reset();
  const c = client();
  const seen = [];
  const ctl = new AbortController();
  server.state.keepOpen = true;
  try {
    for await (const m of c.mcp.notifications({ signal: ctl.signal })) {
      seen.push(m.params.text);
      if (seen.length === 3) ctl.abort();
    }
    assert.fail("abort should end the iterator with an error");
  } catch (e) {
    assert.ok(e instanceof FoundgineAbortError, String(e));
  }
  assert.deepEqual(seen, ["héllo ✓", "second", "third"]);
  assert.equal(server.state.resumedWith, "2", "resumed after the last delivered event");
  assert.equal(c.mcp.lastEventId, "3");
  await c.close();
});

test("notifications: reconnect:false ends after the first stream", async () => {
  reset();
  const c = client();
  const seen = [];
  for await (const m of c.mcp.notifications({ reconnect: false })) seen.push(m.params.text);
  assert.deepEqual(seen, ["héllo ✓", "second"]);
  assert.equal(server.state.streamRuns, 1);
  await c.close();
});

test("notifications: 410 is FoundgineResyncRequiredError; a stream 404 expires the session", async () => {
  reset();
  const c = client();
  server.state.streamStatus = 410;
  await assert.rejects(async () => { for await (const _ of c.mcp.notifications()) { /* none */ } }, FoundgineResyncRequiredError);
  assert.equal(c.mcp.isReady, true, "410 keeps the session");
  server.state.streamStatus = 404;
  await assert.rejects(async () => { for await (const _ of c.mcp.notifications()) { /* none */ } }, FoundgineSessionExpiredError);
  assert.equal(c.mcp.isReady, false, "404 clears it");
});

test("close sends DELETE once and is idempotent", async () => {
  reset();
  const c = client();
  await c.callTool("echo", {});
  await c.close();
  await c.close();
  await c.mcp.close();
  const deletes = server.state.requests.filter((r) => r.method === "DELETE");
  assert.equal(deletes.length, 1);
  assert.ok(deletes[0].headers["mcp-session-id"]);
  assert.equal(deletes[0].headers["mcp-protocol-version"], undefined, "DELETE has no protocol header, as in the TS SDK");
});
