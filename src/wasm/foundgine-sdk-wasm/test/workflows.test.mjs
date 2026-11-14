import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { FoundgineClient, FoundgineAbortError, FoundgineHttpError, FoundgineWatchTimeoutError, init } from "../js/index.js";
import { startServer } from "./server.mjs";

await init();

let server;
let overrides = () => undefined;
before(async () => {
  server = await startServer({ workflowBehaviour: (...a) => overrides(...a) });
});
after(() => server.close());

const client = (extra = {}) => new FoundgineClient({ baseUrl: server.baseUrl, token: "tok", ...extra });
const script = (key, statuses) => server.state.workflows.set(key, { statuses, polls: 0 });
const fast = { intervalMs: 5 };

test("processes, describe, start, decide, cancel use the right routes and bodies", async () => {
  server.state.requests.length = 0;
  const wf = client().workflows;
  assert.deepEqual(await wf.processes(), ["order", "refund"]);
  const d = await wf.describe("order/x y");
  assert.equal(d.name, "order/x y");
  assert.deepEqual(d.steps[0], { name: "review", requiresApproval: true, hasCompensation: false, approvalTimeoutSeconds: 3600 });
  assert.deepEqual(d.steps[1], { name: "ship", requiresApproval: false, hasCompensation: false, approvalTimeoutSeconds: null });
  const started = await wf.start("order", "k 1", { sku: 1 });
  assert.equal(started.workflowId, "wf-k 1");
  assert.equal(started.alreadyExisted, false);
  await wf.start("order", "k2");
  await wf.decide("order", "k1", "review", false, { reason: "too risky" });
  await wf.decide("order", "k1", "review", true);
  await wf.cancel("order", "k1");

  const by = (pred) => server.state.requests.filter(pred);
  assert.ok(by((r) => r.url === "/foundgine/workflows/order%2Fx%20y").length === 1, "path segment is percent-encoded");
  const startBodies = by((r) => r.method === "POST" && r.body?.businessKey).map((r) => r.body);
  assert.deepEqual(startBodies, [{ businessKey: "k 1", payload: { sku: 1 } }, { businessKey: "k2", payload: {} }]);
  const decisions = by((r) => r.url.endsWith("/decision")).map((r) => r.body);
  assert.deepEqual(decisions, [{ step: "review", approved: false, reason: "too risky" }, { step: "review", approved: true }]);
  const cancel = by((r) => r.url.endsWith("/cancel"))[0];
  assert.equal(cancel.body, undefined);
  assert.equal(cancel.headers.authorization, "Bearer tok");
});

test("an HTTP failure carries the server's message", async () => {
  await assert.rejects(client().workflows.status("order", "missing"), (e) => e instanceof FoundgineHttpError && e.status === 404 && e.message === "HTTP 404: workflow not found");
});

test("watch: distinct statuses only, updates() replays history, done resolves with the final status", async () => {
  script("w1", ["RUNNING", "RUNNING", "RUNNING", "COMPLETED"]);
  const seenByCallback = [];
  const watch = client().workflows.watch("order", "w1", { ...fast, onUpdate: (s) => { seenByCallback.push(s.temporalStatus); throw new Error("callback failure is swallowed"); } });
  const final = await watch.done;
  assert.equal(final.temporalStatus, "COMPLETED");
  assert.equal(final.isFinished, true);
  assert.deepEqual(seenByCallback, ["RUNNING", "COMPLETED"]);
  const replay = [];
  for await (const s of watch.updates()) replay.push(s.temporalStatus);
  assert.deepEqual(replay, ["RUNNING", "COMPLETED"], "history replays after the watch has finished");
  assert.equal(watch.latest.temporalStatus, "COMPLETED");
});

test("watch: updates() follows live and ends when the workflow finishes", async () => {
  script("w2", ["RUNNING", "RUNNING", "FAILED"]);
  const watch = client().workflows.watch("order", "w2", fast);
  const seen = [];
  for await (const s of watch.updates()) seen.push(s.temporalStatus);
  assert.deepEqual(seen, ["RUNNING", "FAILED"], "FAILED is terminal: it is a final status, not an error");
});

test("watch: a 404 inside the grace window is tolerated, after it the watch fails", async () => {
  const c = client();
  setTimeout(() => script("late", ["COMPLETED"]), 40);
  const ok = await c.workflows.watch("order", "late", { ...fast, notFoundGraceMs: 2000 }).done;
  assert.equal(ok.temporalStatus, "COMPLETED");
  await assert.rejects(c.workflows.watch("order", "never", { ...fast, notFoundGraceMs: 30 }).done, (e) => e instanceof FoundgineHttpError && e.status === 404);
});

test("watch: 401 is fatal at once and is not reported to onError", async () => {
  overrides = (req, parts) => (parts[1] === "denied" ? { status: 401, body: { error: "bad token" } } : undefined);
  const errors = [];
  const t0 = Date.now();
  await assert.rejects(client().workflows.watch("order", "denied", { intervalMs: 1000, onError: (e, n) => errors.push([e.status, n]) }).done, (e) => e.status === 401 && e.message === "HTTP 401: bad token");
  assert.ok(Date.now() - t0 < 500, "failed without waiting an interval");
  assert.deepEqual(errors, []);
  overrides = () => undefined;
});

test("watch: errors are counted, reported, reset by a success, and fatal past maxConsecutiveErrors", async () => {
  let hits = 0;
  overrides = (req, parts) => (parts[1] === "flaky" ? (++hits % 3 === 0 ? undefined : { status: 503, body: { error: "unavailable" } }) : undefined);
  script("flaky", ["RUNNING", "COMPLETED"]);
  const reported = [];
  const ok = await client().workflows.watch("order", "flaky", { ...fast, maxConsecutiveErrors: 5, onError: (e, n) => reported.push(n) }).done;
  assert.equal(ok.temporalStatus, "COMPLETED");
  assert.deepEqual(reported, [1, 2, 1, 2], "the streak restarts after a successful poll");

  overrides = (req, parts) => (parts[1] === "down" ? { status: 503, body: { error: "unavailable" } } : undefined);
  const counts = [];
  await assert.rejects(client().workflows.watch("order", "down", { ...fast, maxConsecutiveErrors: 2, onError: (e, n) => counts.push(n) }).done, (e) => e.status === 503);
  assert.deepEqual(counts, [1, 2, 3], "the count that tips it over is reported before the failure");
  overrides = () => undefined;
});

test("watch: timeout error carries the last status", async () => {
  script("slowwf", ["RUNNING"]);
  await assert.rejects(client().workflows.watch("order", "slowwf", { ...fast, timeoutMs: 60 }).done, (e) => e instanceof FoundgineWatchTimeoutError && e.lastStatus?.temporalStatus === "RUNNING" && /Timed out/.test(e.message));
});

test("watch: stop() rejects done with FoundgineAbortError and updates() surfaces it; a failed watch needs no listener", async () => {
  script("stopme", ["RUNNING"]);
  const watch = client().workflows.watch("order", "stopme", fast);
  await new Promise((r) => setTimeout(r, 30));
  watch.stop();
  await assert.rejects(watch.done, FoundgineAbortError);
  await assert.rejects(async () => { for await (const _ of watch.updates()) { /* history */ } }, FoundgineAbortError);
  // No one awaits `done` here: this must not crash the process with an unhandled rejection.
  client().workflows.watch("order", "absent", { ...fast, notFoundGraceMs: 0 });
  await new Promise((r) => setTimeout(r, 100));
});

test("watch honours an external AbortSignal", async () => {
  script("sig", ["RUNNING"]);
  const ctl = new AbortController();
  const watch = client().workflows.watch("order", "sig", { ...fast, signal: ctl.signal });
  setTimeout(() => ctl.abort(), 30);
  await assert.rejects(watch.done, FoundgineAbortError);
});

test("waitForCompletion returns the final status, throws on timeout, and propagates errors", async () => {
  script("wc1", ["RUNNING", "CANCELLED"]);
  const done = await client().workflows.waitForCompletion("order", "wc1", fast);
  assert.equal(done.temporalStatus, "CANCELLED");
  script("wc2", ["RUNNING"]);
  await assert.rejects(client().workflows.waitForCompletion("order", "wc2", { ...fast, timeoutMs: 40 }), (e) => e instanceof FoundgineWatchTimeoutError && e.lastStatus.temporalStatus === "RUNNING");
  await assert.rejects(client().workflows.waitForCompletion("order", "nope", fast), (e) => e.status === 404);
});

test("startAndWatch starts, then watches", async () => {
  script("wf-sw", ["COMPLETED"]);
  script("sw", ["RUNNING", "COMPLETED"]);
  const { start, watch } = await client().workflows.startAndWatch("order", "sw", { a: 1 }, fast);
  assert.equal(start.workflowId, "wf-sw");
  assert.equal((await watch.done).temporalStatus, "COMPLETED");
});

test("status parsing rejects a body without temporalStatus", async () => {
  overrides = (req, parts) => (parts[1] === "bad" ? { status: 200, body: { workflowId: "x" } } : undefined);
  await assert.rejects(client().workflows.status("order", "bad"), /missing workflowId or temporalStatus/);
  overrides = () => undefined;
});
