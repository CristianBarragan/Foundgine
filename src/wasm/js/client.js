// FoundgineClient: the entry point, mirroring the TypeScript SDK: `client.mcp`, `client.workflows`, `listTools`, `callTool`, `close`.

import { core, init, resolveFetch } from "./runtime.js";
import { McpSession } from "./mcp.js";
import { WorkflowClient } from "./workflows.js";
import { call } from "./runtime.js";

export class FoundgineClient {
  #closed = false;

  /**
   * @param {{
   *   baseUrl: string,
   *   token?: string | (() => string | undefined | Promise<string | undefined>),
   *   fetch?: typeof fetch,
   *   mcpPath?: string,
   *   protocolVersion?: string,
   *   userAgent?: string,
   * }} options
   * The WebAssembly module must be loaded first (`await init()`), or use `FoundgineClient.create`.
   */
  constructor(options) {
    const baseUrl = call(() => core().normalizeBaseUrl(options.baseUrl));
    const fetchImpl = resolveFetch(options.fetch);
    this.mcp = new McpSession({
      baseUrl,
      fetch: fetchImpl,
      token: options.token,
      path: options.mcpPath,
      protocolVersion: options.protocolVersion,
      userAgent: options.userAgent,
    });
    this.workflows = new WorkflowClient({ baseUrl, fetch: fetchImpl, token: options.token });
  }

  /** Load the WebAssembly module (once) and construct a client. `wasm` is passed to `init`. */
  static async create(options, wasm) {
    await init(wasm);
    return new FoundgineClient(options);
  }

  /** Canonical cross-SDK name for listing MCP capabilities. */
  listCapabilities(options) {
    return this.mcp.listTools(options);
  }

  /** Canonical cross-SDK name for executing an MCP tool. */
  execute(name, args, options) {
    return this.mcp.callTool(name, args, options);
  }

  /** Backwards-compatible MCP naming. */
  listTools(options) {
    return this.listCapabilities(options);
  }

  /** Backwards-compatible MCP naming. */
  callTool(name, args, options) {
    return this.execute(name, args, options);
  }

  /** Top-level notification stream, matching the TypeScript and Python clients. */
  notifications(options) {
    return this.mcp.notifications(options);
  }

  /** Close the MCP session. Runs once; later calls do nothing. */
  async close() {
    if (this.#closed) return;
    this.#closed = true;
    await this.mcp.close();
  }
}
