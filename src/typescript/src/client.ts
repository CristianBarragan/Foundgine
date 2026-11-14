import { FoundgineError } from "./errors.js";
import { McpSession, type McpOptions } from "./mcp.js";
import type { TokenProvider, FetchLike } from "./http.js";
import { WorkflowClient } from "./workflows.js";
import type { WorkflowWatchOptions } from "./workflows.js";

export interface FoundgineClientOptions {
  readonly baseUrl: string;
  readonly token?: TokenProvider;
  readonly fetch?: FetchLike;
  readonly mcpPath?: string;
  readonly protocolVersion?: string;
}
export interface Tool { readonly name: string; readonly description?: string; readonly inputSchema: Record<string, unknown>; readonly raw: Record<string, unknown> }
export interface ToolResult { readonly content: readonly Record<string, unknown>[]; readonly structuredContent?: unknown; readonly isError: boolean; readonly raw: Record<string, unknown> }

/** Thin client for Foundgine's MCP and workflow HTTP surfaces. */
export class FoundgineClient {
  readonly mcp: McpSession;
  readonly workflows: WorkflowClient;
  readonly baseUrl: string;
  readonly #fetch: FetchLike;
  #closed = false;

  constructor(options: FoundgineClientOptions) {
    if (!/^https?:\/\//i.test(options.baseUrl)) throw new TypeError("baseUrl must start with http:// or https://");
    this.baseUrl = options.baseUrl.replace(/\/$/, "");
    this.#fetch = options.fetch ?? globalThis.fetch.bind(globalThis);
    const mcpOptions: McpOptions = { fetch: this.#fetch, token: options.token, ...(options.mcpPath ? { path: options.mcpPath } : {}), ...(options.protocolVersion ? { protocolVersion: options.protocolVersion } : {}) };
    this.mcp = new McpSession(this.baseUrl, mcpOptions);
    this.workflows = new WorkflowClient(this.baseUrl, this.#fetch, options.token);
  }

  async listCapabilities(): Promise<Tool[]> {
    const tools: Tool[] = [];
    let cursor: string | undefined;
    do {
      const result = await this.mcp.request<{ tools?: unknown; nextCursor?: unknown }>("tools/list", cursor ? { cursor } : {});
      if (!result || typeof result !== "object") throw new FoundgineError("tools/list returned no result object");
      if (Array.isArray(result.tools)) {
        for (const item of result.tools) {
          if (!item || typeof item !== "object" || Array.isArray(item)) continue;
          const raw = item as Record<string, unknown>;
          if (typeof raw.name !== "string") continue;
          tools.push({ name: raw.name, ...(typeof raw.description === "string" ? { description: raw.description } : {}), inputSchema: raw.inputSchema && typeof raw.inputSchema === "object" && !Array.isArray(raw.inputSchema) ? raw.inputSchema as Record<string, unknown> : {}, raw });
        }
      }
      cursor = typeof result.nextCursor === "string" && result.nextCursor.length ? result.nextCursor : undefined;
    } while (cursor);
    return tools;
  }

  async execute(name: string, args: Record<string, unknown> = {}, options: { signal?: AbortSignal } = {}): Promise<ToolResult> {
    const result = await this.mcp.request<unknown>("tools/call", { name, arguments: args }, options);
    if (!result || typeof result !== "object" || Array.isArray(result)) throw new FoundgineError("tools/call returned no result object");
    const raw = result as Record<string, unknown>;
    return { content: Array.isArray(raw.content) ? raw.content.filter((x): x is Record<string, unknown> => !!x && typeof x === "object" && !Array.isArray(x)) : [], ...(Object.hasOwn(raw, "structuredContent") ? { structuredContent: raw.structuredContent } : {}), isError: raw.isError === true, raw };
  }

  notifications(options: { signal?: AbortSignal; lastEventId?: string; reconnectDelayMs?: number } = {}): AsyncGenerator<Record<string, unknown>> {
    return this.mcp.notifications(options);
  }

  async close(): Promise<void> {
    if (this.#closed) return;
    this.#closed = true;
    await this.mcp.close();
  }
}
