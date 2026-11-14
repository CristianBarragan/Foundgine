// MCP session over HTTP. The protocol (headers, ids, session rules, error mapping) is in the Rust core;
// this class does the I/O the core leaves to the host: `fetch`, tokens, one shared initialize, abort, the stream body.

import { FoundgineAbortError, FoundgineError } from "./errors.js";
import { call, core, isAbortLike, headersToPairs, raceAbort, resolveToken, send, sleep, throwIfAborted } from "./runtime.js";

const DEFAULT_RECONNECT_DELAY_MS = 1000;

export class McpSession {
  #core;
  #fetch;
  #token;
  #initializing = null;

  /** @param {{ baseUrl: string, fetch: Function, token?: unknown, path?: string, protocolVersion?: string, userAgent?: string }} o */
  constructor(o) {
    this.#fetch = o.fetch;
    this.#token = o.token;
    this.#core = new (core().McpCore)(o.baseUrl, o.path, o.protocolVersion, o.userAgent);
  }

  get url() {
    return this.#core.url;
  }
  /** The `Mcp-Session-Id`, once initialized and if the server issued one. */
  get sessionId() {
    return this.#core.session ?? undefined;
  }
  get isReady() {
    return this.#core.isReady;
  }
  get lastEventId() {
    return this.#core.lastEventId ?? undefined;
  }

  /** Initialize now. Concurrent callers share one handshake; a ready session is left alone. */
  async initialize(options = {}) {
    if (this.#core.isReady) return;
    if (!this.#initializing) {
      this.#initializing = this.#handshake().finally(() => {
        this.#initializing = null;
      });
    }
    await raceAbort(this.#initializing, options.signal);
  }

  async #handshake() {
    const token = await resolveToken(this.#token);
    const { request, id } = JSON.parse(this.#core.initializeRequest(token));
    const response = await send(this.#fetch, request);
    const ackToken = await resolveToken(this.#token); // the ack is its own request: resolve the token again
    const next = JSON.parse(call(() => this.#core.completeInitialize(id, JSON.stringify(response), ackToken)));
    const ack = await send(this.#fetch, next);
    call(() => this.#core.checkNotificationResponse(JSON.stringify(ack)));
  }

  /** Send a JSON-RPC request and return its `result` (`undefined` when the server sent none). */
  async request(method, params, options = {}) {
    const { signal } = options;
    throwIfAborted(signal);
    await this.initialize({ signal });
    const token = await resolveToken(this.#token);
    const { request, id } = call(() => JSON.parse(this.#core.requestMessage(method, params === undefined ? undefined : JSON.stringify(params), token)));
    let response;
    try {
      response = await send(this.#fetch, request, signal);
    } catch (e) {
      if (e instanceof FoundgineAbortError) await this.#bestEffortCancel(id);
      throw e;
    }
    const result = call(() => this.#core.parseResponse(id, JSON.stringify(response)));
    return result === undefined ? undefined : JSON.parse(result);
  }

  async #bestEffortCancel(id) {
    try {
      const token = await resolveToken(this.#token);
      await send(this.#fetch, JSON.parse(this.#core.cancelRequest(id, token)));
    } catch {
      /* best effort */
    }
  }

  /** Send a JSON-RPC notification (no response expected). */
  async notify(method, params, options = {}) {
    await this.initialize(options);
    const token = await resolveToken(this.#token);
    const request = call(() => JSON.parse(this.#core.notifyMessage(method, params === undefined ? undefined : JSON.stringify(params), token)));
    const response = await send(this.#fetch, request, options.signal);
    call(() => this.#core.checkNotificationResponse(JSON.stringify(response)));
  }

  /** All tools, following `nextCursor` until the server stops sending one. */
  async listTools(options = {}) {
    const tools = [];
    let cursor;
    do {
      const params = JSON.parse(core().listToolsParams(cursor));
      const result = await this.request("tools/list", params, options);
      const page = JSON.parse(call(() => core().parseToolsPage(result === undefined ? undefined : JSON.stringify(result))));
      tools.push(...page.tools);
      cursor = page.nextCursor;
    } while (cursor);
    return tools;
  }

  /** Call a tool. A tool that fails is data (`isError: true`), not a thrown error. */
  async callTool(name, args, options = {}) {
    const params = JSON.parse(call(() => core().callToolParams(name, args === undefined ? undefined : JSON.stringify(args))));
    const result = await this.request("tools/call", params, options);
    return JSON.parse(call(() => core().parseToolResult(result === undefined ? undefined : JSON.stringify(result))));
  }

  /**
   * Server-to-client messages from the per-session SSE stream, as an async iterator. It resumes with
   * `Last-Event-ID` after the server closes the stream, waiting the server's `retry:` (or one second) first.
   * Pass `reconnect: false` to end after the first stream. Errors (expired session, 410 resync, network) are thrown;
   * aborting the signal throws `FoundgineAbortError`.
   */
  async *notifications(options = {}) {
    const { signal, reconnect = true, lastEventId, reconnectDelayMs = DEFAULT_RECONNECT_DELAY_MS } = options;
    throwIfAborted(signal);
    await this.initialize({ signal });
    if (lastEventId !== undefined) this.#core.lastEventId = lastEventId;
    let retryMs = reconnectDelayMs;
    for (;;) {
      const token = await resolveToken(this.#token);
      const request = JSON.parse(this.#core.streamRequest(token));
      let res;
      try {
        res = await this.#fetch(request.url, { method: "GET", headers: Object.fromEntries(request.headers), signal });
      } catch (e) {
        if (isAbortLike(e, signal)) throw new FoundgineAbortError();
        throw e;
      }
      if (!res.ok) {
        const body = await res.text().catch(() => "");
        call(() => this.#core.checkStreamStatus(JSON.stringify({ status: res.status, headers: headersToPairs(res.headers), body })));
        throw new FoundgineError(`Unexpected stream status ${res.status}`);
      }
      if (!res.body) throw new FoundgineError("The stream response has no body");
      const sse = new (core().SseCore)();
      try {
        const reader = res.body.getReader();
        try {
          for (;;) {
            let chunk;
            try {
              chunk = await reader.read();
            } catch (e) {
              if (isAbortLike(e, signal)) throw new FoundgineAbortError();
              throw e;
            }
            if (chunk.done) break;
            const events = JSON.parse(call(() => sse.pushBytes(chunk.value)));
            if (sse.retryMs !== undefined) retryMs = sse.retryMs;
            for (const ev of events) {
              const message = this.#core.recordStreamEvent(ev.id ?? undefined, ev.event ?? undefined, ev.data);
              if (message !== undefined) yield JSON.parse(message);
            }
          }
        } finally {
          reader.cancel().catch(() => {});
        }
      } finally {
        sse.free();
      }
      if (!reconnect) return;
      await sleep(retryMs, signal);
    }
  }

  /** End the session: sends `DELETE` once (failures are ignored). Safe to call again. */
  async close() {
    const token = await resolveToken(this.#token);
    const text = this.#core.closeRequest(token);
    if (text === undefined) return;
    try {
      await send(this.#fetch, JSON.parse(text));
    } catch {
      /* best effort */
    }
  }
}

