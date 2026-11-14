// A small in-process Foundgine server for the tests: MCP over HTTP (JSON-RPC, session header, paged tools/list,
// SSE notification stream with Last-Event-ID) and the workflow routes. Every request is recorded.

import http from "node:http";

export async function startServer(opts = {}) {
  const state = {
    requests: [],
    sessions: new Set(),
    nextSession: 1,
    toolPages: opts.toolPages ?? [
      { tools: [{ name: "echo", description: "Echo", inputSchema: { type: "object" } }, { nope: true }], nextCursor: "p2" },
      { tools: [{ name: "add", inputSchema: { type: "object" } }] },
    ],
    streamRuns: 0,
    expireNext: false,
    workflows: new Map(), // key -> { statuses: [...], polls: 0 }
    workflowBehaviour: opts.workflowBehaviour ?? (() => undefined),
  };

  const server = http.createServer(async (req, res) => {
    const chunks = [];
    for await (const c of req) chunks.push(c);
    const raw = Buffer.concat(chunks).toString("utf8");
    const body = raw ? JSON.parse(raw) : undefined;
    const rec = { method: req.method, url: req.url, headers: req.headers, body };
    state.requests.push(rec);
    const json = (status, value, headers = {}) => {
      res.writeHead(status, { "content-type": "application/json", ...headers });
      res.end(JSON.stringify(value));
    };

    if (req.url === "/mcp") return mcp(req, res, rec, state, json);
    if (req.url.startsWith("/foundgine/workflows")) return workflows(req, res, rec, state, json);
    json(404, { error: "no such route" });
  });

  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const { port } = server.address();
  return {
    state,
    baseUrl: `http://127.0.0.1:${port}`,
    close: () => new Promise((r) => (server.closeAllConnections?.(), server.close(r))),
  };
}

function mcp(req, res, rec, state, json) {
  const sid = req.headers["mcp-session-id"];
  if (req.method === "POST" && rec.body?.method === "initialize") {
    const id = `sess-${state.nextSession++}`;
    state.sessions.add(id);
    return json(200, { jsonrpc: "2.0", id: rec.body.id, result: { protocolVersion: "2025-06-18", capabilities: {} } }, { "mcp-session-id": id });
  }
  if (sid && !state.sessions.has(sid)) return json(404, { error: "unknown session" });
  if (state.expireNext) {
    state.expireNext = false;
    state.sessions.delete(sid);
    return json(404, { error: "session expired" });
  }
  if (req.method === "DELETE") {
    state.sessions.delete(sid);
    res.writeHead(204);
    return res.end();
  }
  if (req.method === "GET") {
    const run = ++state.streamRuns;
    const last = req.headers["last-event-id"];
    if (state.streamStatus) return json(state.streamStatus, { error: "nope" });
    res.writeHead(200, { "content-type": "text/event-stream" });
    // Run 1 sends events 1 and 2 (with a split multi-byte character), run 2 resumes after Last-Event-ID.
    const frame = (id, msg) => `id: ${id}\ndata: ${JSON.stringify(msg)}\n\n`;
    if (run === 1) {
      res.write("retry: 20\n\n");
      res.write(frame(1, { jsonrpc: "2.0", method: "notifications/message", params: { text: "héllo ✓" } }));
      const second = Buffer.from(frame(2, { jsonrpc: "2.0", method: "notifications/message", params: { text: "second" } }));
      res.write(second.subarray(0, 7));
      setTimeout(() => res.end(second.subarray(7)), 10);
    } else {
      state.resumedWith = last;
      res.write(frame(3, { jsonrpc: "2.0", method: "notifications/message", params: { text: "third" } }));
      if (!state.keepOpen) res.end();
    }
    return;
  }
  const m = rec.body;
  if (m.method === "notifications/initialized" || m.method === "notifications/cancelled") {
    res.writeHead(202);
    return res.end();
  }
  const reply = (result) => json(200, { jsonrpc: "2.0", id: m.id, result });
  if (m.method === "tools/list") {
    const page = m.params?.cursor === "p2" ? state.toolPages[1] : state.toolPages[0];
    return reply(page);
  }
  if (m.method === "tools/call") {
    if (m.params.name === "boom") return reply({ content: [{ type: "text", text: "failed" }], isError: true });
    if (m.params.name === "slow") return; // never answers: used for abort
    return reply({ content: [{ type: "text", text: JSON.stringify(m.params.arguments) }], structuredContent: { echoed: m.params.arguments } });
  }
  return json(200, { jsonrpc: "2.0", id: m.id, error: { code: -32601, message: "Method not found", data: { method: m.method } } });
}

function workflows(req, res, rec, state, json) {
  const parts = req.url.split("?")[0].split("/").slice(3).map(decodeURIComponent); // after /foundgine/workflows
  const override = state.workflowBehaviour(req, parts, state);
  if (override) return json(override.status, override.body ?? {});
  if (parts.length === 0) return json(200, { processes: ["order", "refund"] });
  if (parts.length === 1 && req.method === "GET") {
    return json(200, { name: parts[0], steps: [{ name: "review", requiresApproval: true, hasCompensation: false, approvalTimeoutSeconds: 3600 }, { name: "ship" }] });
  }
  if (parts.length === 1 && req.method === "POST") {
    return json(200, { workflowId: `wf-${rec.body.businessKey}`, status: "Started", alreadyExisted: false });
  }
  const [process, key, action] = parts;
  if (action === "decision" || action === "cancel") {
    res.writeHead(204);
    return res.end();
  }
  const wf = state.workflows.get(key);
  if (!wf) return json(404, { error: "workflow not found" });
  const status = wf.statuses[Math.min(wf.polls++, wf.statuses.length - 1)];
  return json(200, { workflowId: `wf-${key}`, temporalStatus: status, process: { name: process } });
}
