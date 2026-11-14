export type JsonObject = { [key: string]: unknown };
export type TokenSource = string | undefined | (() => string | undefined | Promise<string | undefined>);

export interface RequestOptions {
  signal?: AbortSignal;
}

export interface FoundgineClientOptions {
  /** Must start with `http://` or `https://`. */
  baseUrl: string;
  /** A bearer token, or a function called once per HTTP request. A blank token sends no `Authorization` header. */
  token?: TokenSource;
  /** Defaults to `globalThis.fetch`. Inject your own for tests, proxies or runtimes without a global fetch. */
  fetch?: typeof fetch;
  /** MCP endpoint path. Default `/mcp`. */
  mcpPath?: string;
  protocolVersion?: string;
  userAgent?: string;
}

/** Load the WebAssembly module once. Call before `new FoundgineClient(...)`, or use `FoundgineClient.create`. */
export function init(input?: BufferSource | WebAssembly.Module | Response | Promise<Response> | URL | string): Promise<void>;

export interface Tool {
  name: string;
  description: string | null;
  inputSchema: JsonObject;
  raw: JsonObject;
}

export interface ToolResult {
  content: JsonObject[];
  /** Present only when the server sent `structuredContent`. */
  structuredContent?: unknown;
  isError: boolean;
  raw: JsonObject;
}

export interface JsonRpcMessage extends JsonObject {
  jsonrpc?: string;
  method?: string;
  params?: unknown;
}

export class McpSession {
  readonly url: string;
  readonly sessionId: string | undefined;
  readonly isReady: boolean;
  readonly lastEventId: string | undefined;
  initialize(options?: RequestOptions): Promise<void>;
  request(method: string, params?: unknown, options?: RequestOptions): Promise<unknown>;
  notify(method: string, params?: unknown, options?: RequestOptions): Promise<void>;
  listTools(options?: RequestOptions): Promise<Tool[]>;
  callTool(name: string, args?: unknown, options?: RequestOptions): Promise<ToolResult>;
  notifications(options?: RequestOptions & { reconnect?: boolean; lastEventId?: string; reconnectDelayMs?: number }): AsyncGenerator<JsonRpcMessage, void, undefined>;
  close(): Promise<void>;
}

export interface WorkflowStepDescription {
  name: string;
  requiresApproval: boolean;
  hasCompensation: boolean;
  approvalTimeoutSeconds: number | null;
}
export interface WorkflowProcessDescription {
  name: string;
  steps: WorkflowStepDescription[];
}
export interface WorkflowStartResult {
  workflowId: string;
  status: string;
  alreadyExisted: boolean;
  raw: JsonObject;
}
export interface WorkflowStatus {
  workflowId: string;
  temporalStatus: string;
  /** Passed through untouched; absent when the server omitted it. */
  process?: unknown;
  raw: JsonObject;
  isFinished: boolean;
}

export interface WatchOptions extends RequestOptions {
  /** Default 1000. */
  intervalMs?: number;
  /** No timeout by default. On expiry `done` rejects with `FoundgineWatchTimeoutError`. */
  timeoutMs?: number;
  /** How long a 404 is tolerated after the watch starts. Default 5000. */
  notFoundGraceMs?: number;
  /** Default 5. */
  maxConsecutiveErrors?: number;
  onUpdate?: (status: WorkflowStatus) => void | Promise<void>;
  onError?: (error: Error, consecutiveErrors: number) => void | Promise<void>;
}

export class WorkflowWatch {
  /** Settles with the final status; rejects on failure, timeout or `stop()`. */
  readonly done: Promise<WorkflowStatus>;
  readonly latest: WorkflowStatus | undefined;
  stop(): void;
  updates(): AsyncGenerator<WorkflowStatus, void, undefined>;
}

export class WorkflowClient {
  processes(options?: RequestOptions): Promise<string[]>;
  describe(process: string, options?: RequestOptions): Promise<WorkflowProcessDescription>;
  start(process: string, businessKey: string, payload?: unknown, options?: RequestOptions): Promise<WorkflowStartResult>;
  status(process: string, businessKey: string, options?: RequestOptions): Promise<WorkflowStatus>;
  /** No caller-identity argument: the server stamps the decision subject from its own security context. */
  decide(process: string, businessKey: string, step: string, approved: boolean, options?: RequestOptions & { reason?: string }): Promise<void>;
  cancel(process: string, businessKey: string, options?: RequestOptions): Promise<void>;
  watch(process: string, businessKey: string, options?: WatchOptions): WorkflowWatch;
  waitForCompletion(process: string, businessKey: string, options?: RequestOptions & { intervalMs?: number; timeoutMs?: number }): Promise<WorkflowStatus>;
  startAndWatch(process: string, businessKey: string, payload?: unknown, options?: WatchOptions): Promise<{ start: WorkflowStartResult; watch: WorkflowWatch }>;
}

export class FoundgineClient {
  constructor(options: FoundgineClientOptions);
  static create(options: FoundgineClientOptions, wasm?: Parameters<typeof init>[0]): Promise<FoundgineClient>;
  readonly mcp: McpSession;
  readonly workflows: WorkflowClient;
  /** Canonical cross-SDK name for listing MCP capabilities. */
  listCapabilities(options?: RequestOptions): Promise<Tool[]>;
  /** Canonical cross-SDK name for executing an MCP tool. */
  execute(name: string, args?: unknown, options?: RequestOptions): Promise<ToolResult>;
  /** Backwards-compatible MCP naming. */
  listTools(options?: RequestOptions): Promise<Tool[]>;
  /** Backwards-compatible MCP naming. */
  callTool(name: string, args?: unknown, options?: RequestOptions): Promise<ToolResult>;
  /** Top-level notification stream, matching the TypeScript and Python clients. */
  notifications(options?: RequestOptions & { reconnect?: boolean; lastEventId?: string; reconnectDelayMs?: number }): AsyncGenerator<JsonRpcMessage, void, undefined>;
  close(): Promise<void>;
}

export class FoundgineError extends Error {}
export class FoundgineHttpError extends FoundgineError {
  constructor(status: number, message?: string, body?: unknown);
  status: number;
  body: unknown;
}
export class FoundgineSessionExpiredError extends FoundgineHttpError {}
export class FoundgineResyncRequiredError extends FoundgineHttpError {}
export class FoundgineRpcError extends FoundgineError {
  constructor(code: number, message: string, data?: unknown);
  code: number;
  rpcMessage: string;
  data: unknown;
}
export class FoundgineAbortError extends FoundgineError {}
export class FoundgineWatchTimeoutError extends FoundgineError {
  constructor(lastStatus?: unknown, message?: string);
  lastStatus: unknown;
}
