import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { init } from "../js/index.js";
import { core } from "../js/runtime.js";

await init();
const { SseCore } = core();

const parse = (chunks) => {
  const sse = new SseCore();
  const out = [];
  for (const c of chunks) out.push(...JSON.parse(typeof c === "string" ? sse.pushStr(c) : sse.pushBytes(c)));
  sse.free();
  return out;
};

test("CRLF, CR and LF all end lines; event and id fields are read; comments are ignored", () => {
  const events = parse(["id: 7\r\nevent: ping\r\ndata: a\rdata: b\n: comment\n\n"]);
  assert.deepEqual(events, [{ id: "7", event: "ping", data: "a\nb" }]);
});

test("a multi-byte character split across byte chunks is reassembled", () => {
  const bytes = new TextEncoder().encode("data: héllo ✓ 🎉\n\n");
  const events = parse([...bytes].map((b) => new Uint8Array([b])));
  assert.equal(events.length, 1);
  assert.equal(events[0].data, "héllo ✓ 🎉");
});

test("invalid UTF-8 is fatal", () => {
  const sse = new SseCore();
  assert.throws(() => sse.pushBytes(new Uint8Array([0x64, 0x61, 0x74, 0x61, 0x3a, 0x20, 0xff, 0x0a, 0x0a])), /invalid UTF-8/);
});

test("retry: is exposed in milliseconds", () => {
  const sse = new SseCore();
  sse.pushStr("retry: 250\n\n");
  assert.equal(sse.retryMs, 250);
});

// The shared vectors (src/conformance) are the same ones the TypeScript, Python and Rust tests run.
const dir = process.env.FOUNDGINE_CONFORMANCE_DIR ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../conformance");
const file = path.join(dir, "mcp-sse-scenarios.json");
const normalised = (d) => d.replace(/\r\n/g, "\n").replace(/\r/g, "\n");

test("shared conformance vectors: whole frame, one byte at a time, one stream", { skip: existsSync(file) ? false : `no vectors at ${file}` }, () => {
  const v = JSON.parse(readFileSync(file, "utf8"));
  const cases = v.scenarios.flatMap((s) => s.steps.filter((x) => x.op === "format")).map((x) => ({ seq: x.sequence, data: x.data, frame: x.expect }));
  assert.ok(cases.length > 0, "no format vectors found: the test would pass silently");
  const enc = new TextEncoder();
  for (const c of cases) {
    const whole = parse([c.frame]);
    assert.equal(whole.length, 1, `seq ${c.seq}`);
    assert.equal(whole[0].id, c.seq);
    assert.equal(whole[0].event, null, `seq ${c.seq}: injected event: must stay payload`);
    assert.equal(whole[0].data, normalised(c.data));
    const bytewise = parse([...enc.encode(c.frame)].map((b) => new Uint8Array([b])));
    assert.deepEqual(bytewise, whole, `seq ${c.seq} one byte at a time`);
  }
  const all = parse([enc.encode(cases.map((c) => c.frame).join(""))]);
  assert.equal(all.length, cases.length);
  all.forEach((e, i) => assert.equal(e.data, normalised(cases[i].data)));
});
