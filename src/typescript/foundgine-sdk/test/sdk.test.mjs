import test from 'node:test';
import assert from 'node:assert/strict';
import { FoundgineClient, FoundgineAbortError, FoundgineHttpError, FoundgineRpcError, FoundgineSessionExpiredError, FoundgineResyncRequiredError, SseParser } from '../dist/index.js';

function mockFetch(handler) {
  const calls = [];
  const fetch = async (url, init = {}) => {
    const call = { url: String(url), method: init.method ?? 'GET', headers: new Headers(init.headers), body: init.body ? JSON.parse(String(init.body)) : undefined, signal: init.signal };
    calls.push(call);
    return handler(call, calls.length);
  };
  return { fetch, calls };
}
function json(data, status = 200, headers = {}) {
  return new Response(JSON.stringify(data), { status, headers: { 'content-type': 'application/json', ...headers } });
}

test('SSE parser handles CRLF, comments, multiline data and arbitrary chunk boundaries', () => {
  const p = new SseParser();
  const out = [...p.push('id: 9\r'), ...p.push('\nevent: update\ndata: one\ndata: two\n\n')];
  assert.deepEqual(out, [{ id: '9', event: 'update', data: 'one\ntwo' }]);
});

test('SSE parser preserves Unicode separators and supports split UTF-8 bytes', () => {
  const p = new SseParser();
  const bytes = new TextEncoder().encode('data: a\u2028b\u0085c\u000bd\n\ndata: €\n\n');
  const out = [];
  for (const byte of bytes) out.push(...p.push(new Uint8Array([byte])));
  assert.equal(out[0].data, 'a\u2028b\u0085c\u000bd');
  assert.equal(out[1].data, '€');
});

test('initializes once, sends bearer/session headers, pages capabilities and executes tools', async () => {
  let tokenCount = 0;
  const { fetch, calls } = mockFetch((call) => {
    if (call.method === 'DELETE') return new Response(null, { status: 204 });
    if (call.body?.method === 'initialize') return json({ jsonrpc: '2.0', id: call.body.id, result: { protocolVersion: '2025-06-18' } }, 200, { 'Mcp-Session-Id': 's-1' });
    if (call.body?.method === 'notifications/initialized') return new Response(null, { status: 202 });
    if (call.body?.method === 'tools/list' && !call.body.params?.cursor) return json({ jsonrpc: '2.0', id: call.body.id, result: { tools: [{ name: 'one', inputSchema: { type: 'object' } }], nextCursor: 'next' } });
    if (call.body?.method === 'tools/list') return json({ jsonrpc: '2.0', id: call.body.id, result: { tools: [{ name: 'two' }] } });
    if (call.body?.method === 'tools/call') return json({ jsonrpc: '2.0', id: call.body.id, result: { content: [{ type: 'text', text: 'ok' }], isError: false } });
    throw new Error(`Unexpected request ${call.body?.method}`);
  });
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', token: () => `token-${++tokenCount}`, fetch });
  const tools = await client.listCapabilities();
  const result = await client.execute('one', { q: 'x' });
  assert.deepEqual(tools.map(t => t.name), ['one', 'two']);
  assert.equal(result.content[0].text, 'ok');
  assert.equal(calls.filter(c => c.body?.method === 'initialize').length, 1);
  assert.ok(calls.every(c => c.headers.get('authorization')?.startsWith('Bearer token-')));
  assert.ok(calls.filter(c => c.method === 'POST' && c.body?.method !== 'initialize').every(c => c.headers.get('mcp-session-id') === 's-1'));
  await client.close();
});

test('401 remains an HTTP error and JSON-RPC errors remain typed', async () => {
  const unauthorized = mockFetch(() => json({ error: 'no' }, 401));
  const a = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch: unauthorized.fetch });
  await assert.rejects(() => a.listCapabilities(), (e) => e instanceof FoundgineHttpError && e.status === 401);

  let initialized = false;
  const rpc = mockFetch((call) => {
    if (call.body?.method === 'initialize') return json({ jsonrpc: '2.0', id: call.body.id, result: {} });
    if (call.body?.method === 'notifications/initialized') return new Response(null, { status: 202 });
    return json({ jsonrpc: '2.0', id: call.body.id, error: { code: -32601, message: 'missing' } });
  });
  const b = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch: rpc.fetch });
  await assert.rejects(() => b.mcp.request('not-found'), (e) => e instanceof FoundgineRpcError && e.code === -32601);
});

test('404 on an MCP session expires it and the next call initializes again', async () => {
  let initCount = 0;
  let sessionCount = 0;
  const { fetch } = mockFetch((call) => {
    if (call.body?.method === 'initialize') {
      initCount++;
      return json({ jsonrpc: '2.0', id: call.body.id, result: {} }, 200, { 'Mcp-Session-Id': `s-${initCount}` });
    }
    if (call.body?.method === 'notifications/initialized') return new Response(null, { status: 202 });
    if (call.body?.method === 'tools/list' && sessionCount++ === 0) return json({ error: 'expired' }, 404);
    return json({ jsonrpc: '2.0', id: call.body.id, result: { tools: [] } });
  });
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
  await assert.rejects(() => client.listCapabilities(), FoundgineSessionExpiredError);
  await client.listCapabilities();
  assert.equal(initCount, 2);
});

test('SSE parser reads all shared frame-format vectors as individual payload lines', async () => {
  const fs = await import('node:fs/promises');
  const path = new URL('../../../conformance/mcp-sse-scenarios.json', import.meta.url);
  const vectors = JSON.parse(await fs.readFile(path, 'utf8'));
  const scenario = vectors.scenarios.find((s) => s.name === 'frame format keeps payload lines apart');
  assert.ok(scenario);
  for (const step of scenario.steps.filter((s) => s.op === 'format')) {
    const parser = new SseParser();
    const expected = step.expect;
    const whole = parser.push(expected);
    assert.equal(whole.length, 1, step.name ?? step.sequence);
    assert.equal(whole[0].id, step.sequence);
    const expectedData = step.data.replace(/\r\n|\r|\n/g, '\n');
    assert.equal(whole[0].data, expectedData);
    const split = new SseParser();
    const chunked = [];
    for (const char of expected) chunked.push(...split.push(char));
    assert.deepEqual(chunked, whole);
  }
});

test('stream gate statuses preserve the session except 404 and classify 410 as resync', async () => {
  for (const status of [400, 403, 409, 410, 404]) {
    const { fetch, calls } = mockFetch((call) => {
      if (call.body?.method === 'initialize') return json({ jsonrpc: '2.0', id: call.body.id, result: {} }, 200, { 'Mcp-Session-Id': 'gate-session' });
      if (call.body?.method === 'notifications/initialized') return new Response(null, { status: 202 });
      if (call.method === 'GET') return json({ error: `gate ${status}` }, status);
      return json({ jsonrpc: '2.0', id: call.body.id, result: { tools: [] } });
    });
    const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
    await assert.rejects(async () => {
      const iterator = client.notifications();
      await iterator.next();
    }, (error) => {
      if (status === 404) return error instanceof FoundgineSessionExpiredError;
      if (status === 410) return error instanceof FoundgineResyncRequiredError && error.status === 410;
      return error instanceof FoundgineHttpError && error.status === status;
    });
    if (status !== 404) assert.equal(client.mcp.session, 'gate-session');
    else assert.equal(client.mcp.session, undefined);
  }
});


test('notification stream resumes with Last-Event-ID from the last consumed event', async () => {
  let getCount = 0;
  const controller = new AbortController();
  const { fetch, calls } = mockFetch((call) => {
    if (call.body?.method === 'initialize') return json({ jsonrpc: '2.0', id: call.body.id, result: {} }, 200, { 'Mcp-Session-Id': 'stream-session' });
    if (call.body?.method === 'notifications/initialized') return new Response(null, { status: 202 });
    if (call.method === 'GET') {
      getCount++;
      const id = getCount === 1 ? '5' : '6';
      const body = new ReadableStream({ start(c) { c.enqueue(new TextEncoder().encode(`id: ${id}\ndata: ${JSON.stringify({ method: `notice-${id}` })}\n\n`)); c.close(); } });
      return new Response(body, { status: 200, headers: { 'content-type': 'text/event-stream' } });
    }
    throw new Error('unexpected request');
  });
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
  const seen = [];
  for await (const item of client.notifications({ signal: controller.signal, reconnectDelayMs: 0 })) {
    seen.push(item.method);
    if (seen.length === 2) controller.abort();
  }
  assert.deepEqual(seen, ['notice-5', 'notice-6']);
  const gets = calls.filter((call) => call.method === 'GET');
  assert.equal(gets[0].headers.get('last-event-id'), null);
  assert.equal(gets[1].headers.get('last-event-id'), '5');
});


test('AbortSignal aborts an in-flight MCP request and sends notifications/cancelled', async () => {
  let requestStarted;
  const started = new Promise((resolve) => { requestStarted = resolve; });
  const { fetch, calls } = mockFetch((call) => {
    if (call.body?.method === 'initialize') return json({ jsonrpc: '2.0', id: call.body.id, result: {} }, 200, { 'Mcp-Session-Id': 'cancel-session' });
    if (call.body?.method === 'notifications/initialized') return new Response(null, { status: 202 });
    if (call.body?.method === 'tools/list') {
      requestStarted();
      return new Promise((resolve, reject) => {
        call.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true });
      });
    }
    if (call.body?.method === 'notifications/cancelled') return new Response(null, { status: 202 });
    throw new Error('unexpected request');
  });
  const originalFetch = fetch;
  const withSignal = async (url, init = {}) => {
    const call = { url: String(url), method: init.method ?? 'GET', headers: new Headers(init.headers), body: init.body ? JSON.parse(String(init.body)) : undefined, signal: init.signal };
    calls.push(call);
    return originalFetch(url, init);
  };
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch: withSignal });
  const controller = new AbortController();
  const pending = client.mcp.request('tools/list', {}, { signal: controller.signal });
  await started;
  controller.abort();
  await assert.rejects(pending, FoundgineAbortError);
  assert.ok(calls.some((call) => call.body?.method === 'notifications/cancelled'));
});

test('workflow routes encode path segments, authenticate and preserve trusted decision body shape', async () => {
  const calls = [];
  const fetch = async (url, init = {}) => {
    calls.push({ url: String(url), method: init.method, headers: new Headers(init.headers), body: init.body ? JSON.parse(init.body) : undefined });
    if (init.method === 'POST' && String(url).endsWith('/decision')) return new Response(null, { status: 202 });
    if (init.method === 'POST' && String(url).endsWith('/cancel')) return new Response(null, { status: 202 });
    if (init.method === 'POST') return json({ workflowId: 'wf-1', status: 'Started', alreadyExisted: false }, 202);
    return json({ workflowId: 'wf-1', temporalStatus: 'Running', process: { state: 'Waiting' } });
  };
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', token: 'secret', fetch });
  const started = await client.workflows.start('order/approve', 'key ?# ü', { amount: 3 });
  assert.equal(started.workflowId, 'wf-1');
  await client.workflows.decide('order/approve', 'key ?# ü', 'approve', true);
  await client.workflows.cancel('order/approve', 'key ?# ü');
  const status = await client.workflows.status('order/approve', 'key ?# ü');
  assert.equal(status.temporalStatus, 'Running');
  assert.ok(calls[0].url.includes('order%2Fapprove'));
  assert.ok(calls[3].url.includes('key%20%3F%23%20%C3%BC'));
  assert.deepEqual(calls[1].body, { step: 'approve', approved: true });
  assert.ok(calls.every((c) => c.headers.get('authorization') === 'Bearer secret'));
});

test('workflow paths use strict RFC 3986 encoding and normalize repeated base URL slashes', async () => {
  const calls = [];
  const fetch = async (url, init = {}) => {
    calls.push(String(url));
    if (init.method === 'POST') return json({ workflowId: 'wf-1', status: 'Started', alreadyExisted: false }, 202);
    return json({ workflowId: 'wf-1', temporalStatus: 'Completed' });
  };
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test///', fetch });
  await client.workflows.start("a!'()*", "k!'()*");
  await client.workflows.status("a!'()*", "k!'()*");
  assert.equal(calls[0], 'https://foundgine.test/foundgine/workflows/a%21%27%28%29%2A');
  assert.equal(calls[1], 'https://foundgine.test/foundgine/workflows/a%21%27%28%29%2A/k%21%27%28%29%2A');
});

test('workflow watch returns immediately, replays distinct updates and resolves on terminal status', async () => {
  let count = 0;
  const fetch = async () => {
    count++;
    if (count < 3) return json({ workflowId: 'wf-1', temporalStatus: 'Running', process: { step: count } });
    return json({ workflowId: 'wf-1', temporalStatus: 'Completed', process: { step: 2 } });
  };
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
  const started = Date.now();
  const watch = client.workflows.watch('order', 'k', { intervalMs: 1 });
  assert.ok(Date.now() - started < 50);
  const seen = [];
  for await (const update of watch.updates()) seen.push(update.temporalStatus);
  const done = await watch.done;
  assert.equal(done.temporalStatus, 'Completed');
  assert.deepEqual(seen, ['Running', 'Running', 'Completed']);
  assert.equal(watch.isFinished, true);
});

test('workflow watch stop rejects done and updates with typed abort error', async () => {
  const fetch = async () => json({ workflowId: 'wf-1', temporalStatus: 'Running', process: null });
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
  const watch = client.workflows.watch('order', 'k', { intervalMs: 1000 });
  await new Promise((resolve) => setTimeout(resolve, 0));
  watch.stop();
  await assert.rejects(watch.done, FoundgineAbortError);
  await assert.rejects(async () => { for await (const _ of watch.updates()) { /* consume */ } }, FoundgineAbortError);
});

test('waitForCompletion returns terminal status and encodes route identifiers', async () => {
  const fetch = async () => json({ workflowId: 'wf', temporalStatus: 'Completed', process: null });
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
  const result = await client.workflows.waitForCompletion('p', 'k', { intervalMs: 1 });
  assert.equal(result.temporalStatus, 'Completed');
});

test('workflow watch tolerates the initial not-found window and isolates callback failures', async () => {
  let count = 0;
  let updates = 0;
  const fetch = async () => {
    count++;
    if (count === 1) return json({ error: 'not visible yet' }, 404);
    if (count < 4) return json({ workflowId: 'wf', temporalStatus: 'Running', process: { step: 1 } });
    return json({ workflowId: 'wf', temporalStatus: 'Completed', process: { step: 2 } });
  };
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
  const watch = client.workflows.watch('order', 'k', {
    intervalMs: 1,
    notFoundGraceMs: 100,
    onUpdate: () => { updates++; throw new Error('consumer callback failure'); },
  });
  const result = await watch.done;
  assert.equal(result.temporalStatus, 'Completed');
  assert.equal(updates, 2, 'duplicate snapshots are not emitted to onUpdate');
});

test('workflow wait timeout carries the latest status', async () => {
  const fetch = async () => json({ workflowId: 'wf', temporalStatus: 'Running', process: { step: 'waiting' } });
  const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
  await assert.rejects(
    client.workflows.waitForCompletion('order', 'k', { intervalMs: 1, timeoutMs: 2 }),
    (error) => error.name === 'FoundgineWatchTimeoutError' && error.lastStatus?.temporalStatus === 'Running',
  );
});

test('workflow watch treats unauthorized and late not-found as fatal immediately', async () => {
  for (const status of [401, 404]) {
    let calls = 0;
    const fetch = async () => { calls++; return json({ error: 'fatal' }, status); };
    const client = new FoundgineClient({ baseUrl: 'https://foundgine.test', fetch });
    const watch = client.workflows.watch('order', 'k', { intervalMs: 1, notFoundGraceMs: -1 });
    await assert.rejects(watch.done, (error) => error instanceof FoundgineHttpError && error.status === status);
    assert.equal(calls, 1);
  }
});

test("workflow process catalog lists names and describes only public step metadata", async () => {
  const calls = [];
  const fetch = async (url, init = {}) => {
    calls.push([String(url), init.method]);
    if (String(url).endsWith("/foundgine/workflows")) {
      return new Response(JSON.stringify({ processes: ["Order", "Onboarding"] }), { status: 200 });
    }
    return new Response(JSON.stringify({ name: "Order", steps: [{
      name: "reserve", requiresApproval: true, hasCompensation: true, approvalTimeoutSeconds: 30.5,
    }] }), { status: 200 });
  };
  const client = new FoundgineClient({ baseUrl: "https://example.test", fetch });
  assert.deepEqual(await client.workflows.processes(), ["Order", "Onboarding"]);
  const description = await client.workflows.describe("Order");
  assert.equal(description.name, "Order");
  assert.deepEqual(description.steps[0], {
    name: "reserve", requiresApproval: true, hasCompensation: true, approvalTimeoutSeconds: 30.5,
  });
  assert.deepEqual(calls, [
    ["https://example.test/foundgine/workflows", "GET"],
    ["https://example.test/foundgine/workflows/Order", "GET"],
  ]);
});
