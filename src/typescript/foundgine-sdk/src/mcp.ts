import { FoundgineAbortError, FoundgineError, FoundgineHttpError, FoundgineResyncRequiredError, FoundgineRpcError, FoundgineSessionExpiredError } from "./errors.js";
import { errorFor, joinUrl, resolveToken, type FetchLike, type TokenProvider } from "./http.js";
import { SseParser } from "./sse.js";

export const DEFAULT_PROTOCOL_VERSION = "2025-06-18";
const SESSION_HEADER = "Mcp-Session-Id";
const PROTOCOL_HEADER = "MCP-Protocol-Version";

export interface McpOptions {
  readonly path?: string;
  readonly protocolVersion?: string;
  readonly fetch?: FetchLike;
  readonly token?: TokenProvider;
  readonly userAgent?: string;
}
export interface McpRequestOptions { readonly signal?: AbortSignal }

/** Low-level MCP HTTP session. Exposed as `client.mcp` for protocol-level operations. */
export class McpSession {
  readonly url: string;
  readonly #fetch: FetchLike;
  readonly #token: TokenProvider;
  readonly #requestedVersion: string;
  readonly #userAgent: string;
  #version: string | undefined;
  #session: string | undefined;
  #ready = false;
  #initializing: Promise<void> | undefined;
  #nextId = 1;
  #lastEventId: string | undefined;

  constructor(baseUrl: string, options: McpOptions = {}) {
    this.url = joinUrl(baseUrl, options.path ?? "/mcp");
    this.#fetch = options.fetch ?? globalThis.fetch.bind(globalThis);
    this.#token = options.token;
    this.#requestedVersion = options.protocolVersion ?? DEFAULT_PROTOCOL_VERSION;
    this.#userAgent = options.userAgent ?? "foundgine-sdk/3.3.4";
  }

  get session(): string | undefined { return this.#session; }
  get lastEventId(): string | undefined { return this.#lastEventId; }

  async initialize(): Promise<void> {
    if (this.#ready) return;
    if (!this.#initializing) {
      this.#initializing = this.#initializeCore().finally(() => { this.#initializing = undefined; });
    }
    await this.#initializing;
  }

  async request<T = unknown>(method: string, params?: Record<string, unknown>, options: McpRequestOptions = {}): Promise<T> {
    if (options.signal?.aborted) throw new FoundgineAbortError();
    await this.initialize();
    const id = this.#nextId++;
    const message: Record<string, unknown> = { jsonrpc: "2.0", id, method };
    if (params !== undefined) message.params = params;
    let response: Response;
    try {
      response = await this.#fetch(this.url, { method: "POST", headers: await this.#headers(this.#session, true), body: JSON.stringify(message), ...(options.signal ? { signal: options.signal } : {}) });
    } catch (error) {
      if (options.signal?.aborted) {
        await this.#bestEffortCancel(id);
        throw new FoundgineAbortError();
      }
      throw new FoundgineError("The MCP request failed before an HTTP response was received");
    }
    if (!response.ok) {
      const error = await errorFor(response);
      if (response.status === 404 && this.#session) {
        this.#expire(this.#session);
        throw new FoundgineSessionExpiredError(error.message, error.body);
      }
      throw error;
    }
    if (response.status === 202 || response.status === 204) return undefined as T;
    const contentType = response.headers.get("content-type")?.split(";")[0]?.trim().toLowerCase();
    let wire: unknown;
    if (contentType === "text/event-stream") {
      const text = await response.text();
      const parser = new SseParser();
      const event = parser.push(text).find((item) => item.data.length > 0);
      wire = event ? JSON.parse(event.data) as unknown : undefined;
    } else {
      try { wire = await response.json() as unknown; }
      catch (error) { throw new FoundgineError("The server answered with a body that is not JSON", { cause: error }); }
    }
    if (!wire || typeof wire !== "object" || Array.isArray(wire)) throw new FoundgineError("The server answered with a JSON-RPC message that is not an object");
    const rpc = wire as { id?: unknown; result?: T; error?: { code?: unknown; message?: unknown; data?: unknown } };
    if (rpc.error && typeof rpc.error === "object") {
      throw new FoundgineRpcError(typeof rpc.error.code === "number" ? rpc.error.code : -32603, typeof rpc.error.message === "string" ? rpc.error.message : "", rpc.error.data);
    }
    if (rpc.id !== id) throw new FoundgineError("The server returned a JSON-RPC response with an unexpected id");
    return rpc.result as T;
  }

  async notify(method: string, params?: Record<string, unknown>): Promise<void> {
    await this.initialize();
    const message: Record<string, unknown> = { jsonrpc: "2.0", method };
    if (params !== undefined) message.params = params;
    const response = await this.#fetch(this.url, { method: "POST", headers: await this.#headers(this.#session, true), body: JSON.stringify(message) });
    if (!response.ok) throw await errorFor(response);
  }

  async *notifications(options: { signal?: AbortSignal; lastEventId?: string; reconnectDelayMs?: number } = {}): AsyncGenerator<Record<string, unknown>> {
    if (options.lastEventId !== undefined) this.#lastEventId = options.lastEventId;
    const delay = options.reconnectDelayMs ?? 1000;
    while (!options.signal?.aborted) {
      await this.initialize();
      const headers = await this.#headers(this.#session, false, "text/event-stream");
      if (this.#lastEventId !== undefined) headers.set("Last-Event-ID", this.#lastEventId);
      let response: Response;
      try { response = await this.#fetch(this.url, { method: "GET", headers, ...(options.signal ? { signal: options.signal } : {}) }); }
      catch (error) {
        if (options.signal?.aborted) return;
        throw new FoundgineError("The notification stream could not be opened");
      }
      if (!response.ok) {
        const error = await errorFor(response);
        if (response.status === 404) {
          this.#expire(this.#session);
          throw new FoundgineSessionExpiredError(error.message, error.body);
        }
        if (response.status === 410) throw new FoundgineResyncRequiredError(error.message, error.body);
        throw error;
      }
      if (!response.body) throw new FoundgineError("The notification response has no readable body");
      const parser = new SseParser();
      const reader = response.body.getReader();
      try {
        while (!options.signal?.aborted) {
          let next: ReadableStreamReadResult<Uint8Array>;
          try { next = await reader.read(); }
          catch {
            if (options.signal?.aborted) return;
            break; // established stream dropped; reconnect from the last fully consumed event
          }
          if (next.done) break;
          for (const event of parser.push(next.value)) {
            let message: unknown;
            try { message = JSON.parse(event.data) as unknown; } catch { continue; }
            if (event.id !== undefined) this.#lastEventId = event.id;
            if (message && typeof message === "object" && !Array.isArray(message)) yield message as Record<string, unknown>;
          }
        }
      } finally {
        try { await reader.cancel(); } catch { /* release stream even if the peer already closed */ }
        reader.releaseLock();
      }
      if (options.signal?.aborted) return;
      await new Promise<void>((resolve) => setTimeout(resolve, delay));
    }
  }

  async close(): Promise<void> {
    const session = this.#session;
    this.#session = undefined;
    this.#ready = false;
    if (!session) return;
    try {
      const response = await this.#fetch(this.url, { method: "DELETE", headers: await this.#headers(session, false) });
      if (!response.ok) throw await errorFor(response);
    } catch { /* teardown is best effort and idempotent */ }
  }

  async #initializeCore(): Promise<void> {
    const id = this.#nextId++;
    const payload = { jsonrpc: "2.0", id, method: "initialize", params: { protocolVersion: this.#requestedVersion, capabilities: {}, clientInfo: { name: "foundgine-typescript", version: "3.3.4" } } };
    let response: Response;
    try { response = await this.#fetch(this.url, { method: "POST", headers: await this.#headers(undefined, true), body: JSON.stringify(payload) }); }
    catch { throw new FoundgineError("MCP initialization failed before an HTTP response was received"); }
    if (!response.ok) throw await errorFor(response);
    const wire = await response.json() as { id?: unknown; result?: { protocolVersion?: unknown } };
    if (wire.id !== id) throw new FoundgineError("The server returned an unexpected initialize response id");
    const result = wire.result;
    this.#version = typeof result?.protocolVersion === "string" ? result.protocolVersion : this.#requestedVersion;
    this.#session = response.headers.get(SESSION_HEADER) ?? undefined;
    this.#ready = true;
    await this.notifyInitialized();
  }

  async notifyInitialized(): Promise<void> {
    const response = await this.#fetch(this.url, { method: "POST", headers: await this.#headers(this.#session, true), body: JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" }) });
    if (!response.ok) throw await errorFor(response);
  }

  async #headers(session: string | undefined, json: boolean, accept = "application/json, text/event-stream"): Promise<Headers> {
    const headers = new Headers({ Accept: accept, "User-Agent": this.#userAgent });
    const token = await resolveToken(this.#token);
    if (token) headers.set("Authorization", `Bearer ${token}`);
    if (session) headers.set(SESSION_HEADER, session);
    if (this.#ready && this.#version) headers.set(PROTOCOL_HEADER, this.#version);
    if (json) headers.set("Content-Type", "application/json");
    return headers;
  }

  #expire(session: string | undefined): void {
    if (this.#session === session) { this.#session = undefined; this.#ready = false; }
  }

  async #bestEffortCancel(id: number): Promise<void> {
    try {
      await this.notify("notifications/cancelled", { requestId: id, reason: "cancelled by client" });
    } catch { /* preserve the abort outcome */ }
  }
}
