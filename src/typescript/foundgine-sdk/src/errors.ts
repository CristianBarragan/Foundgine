/** Base class for errors intentionally raised by the Foundgine SDK. */
export class FoundgineError extends Error {
  constructor(message: string, options?: ErrorOptions) {
    super(message, options);
    this.name = new.target.name;
  }
}

/** An HTTP response with a non-success status. */
export class FoundgineHttpError extends FoundgineError {
  readonly status: number;
  readonly body: unknown;
  constructor(status: number, message = `HTTP ${status}`, body?: unknown) {
    super(message);
    this.status = status;
    this.body = body;
  }
}

/** The MCP server no longer recognises the current session (HTTP 404). */
export class FoundgineSessionExpiredError extends FoundgineHttpError {
  constructor(message = "MCP session expired or unknown", body?: unknown) {
    super(404, message, body);
  }
}

/** The requested event is no longer present in the replay buffer (HTTP 410). */
export class FoundgineResyncRequiredError extends FoundgineHttpError {
  constructor(message = "Event replay buffer no longer holds the requested event; resync required", body?: unknown) {
    super(410, message, body);
  }
}

/** A JSON-RPC protocol error. Tool-level failures are returned as ToolResult data instead. */
export class FoundgineRpcError extends FoundgineError {
  readonly code: number;
  readonly data: unknown;
  constructor(code: number, message: string, data?: unknown) {
    super(`JSON-RPC error ${code}: ${message}`);
    this.code = code;
    this.data = data;
  }
}

/** The caller aborted an operation. */
export class FoundgineAbortError extends FoundgineError {
  constructor(message = "Operation aborted") { super(message); }
}

/** A workflow watch reached its timeout. */
export class FoundgineWatchTimeoutError extends FoundgineError {
  readonly lastStatus: unknown;
  constructor(lastStatus?: unknown, message = "Timed out waiting for the workflow to finish") {
    super(message);
    this.lastStatus = lastStatus;
  }
}
