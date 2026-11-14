// Error classes. They mirror the TypeScript SDK hierarchy and the Rust `FoundgineError::name()` values.
// The WebAssembly core throws errors as JSON strings; `toError` turns those into these classes.

export class FoundgineError extends Error {
  constructor(message, options) {
    super(message, options);
    this.name = new.target.prototype.name;
  }
}
FoundgineError.prototype.name = "FoundgineError";

export class FoundgineHttpError extends FoundgineError {
  constructor(status, message, body) {
    super(message ?? `HTTP ${status}`);
    this.status = status;
    this.body = body ?? undefined;
  }
}
FoundgineHttpError.prototype.name = "FoundgineHttpError";

export class FoundgineSessionExpiredError extends FoundgineHttpError {
  constructor(message, body) {
    super(404, message ?? "MCP session expired or unknown", body);
  }
}
FoundgineSessionExpiredError.prototype.name = "FoundgineSessionExpiredError";

export class FoundgineResyncRequiredError extends FoundgineHttpError {
  constructor(message, body) {
    super(410, message ?? "Event replay buffer no longer holds the requested event; resync required", body);
  }
}
FoundgineResyncRequiredError.prototype.name = "FoundgineResyncRequiredError";

export class FoundgineRpcError extends FoundgineError {
  constructor(code, message, data) {
    super(`JSON-RPC error ${code}: ${message}`);
    this.code = code;
    this.rpcMessage = message;
    this.data = data ?? undefined;
  }
}
FoundgineRpcError.prototype.name = "FoundgineRpcError";

export class FoundgineAbortError extends FoundgineError {
  constructor(message) {
    super(message ?? "Operation aborted");
  }
}
FoundgineAbortError.prototype.name = "FoundgineAbortError";

export class FoundgineWatchTimeoutError extends FoundgineError {
  constructor(lastStatus, message) {
    super(message ?? "Timed out waiting for the workflow to finish");
    this.lastStatus = lastStatus ?? undefined;
  }
}
FoundgineWatchTimeoutError.prototype.name = "FoundgineWatchTimeoutError";

/** Build the matching class from the core's error object `{name, message, status?, body?, code?, data?, lastStatus?}`. */
export function errorFromObject(o) {
  switch (o.name) {
    case "FoundgineHttpError":
      return new FoundgineHttpError(o.status, o.message, o.body ?? undefined);
    case "FoundgineSessionExpiredError":
      return new FoundgineSessionExpiredError(o.message, o.body ?? undefined);
    case "FoundgineResyncRequiredError":
      return new FoundgineResyncRequiredError(o.message, o.body ?? undefined);
    case "FoundgineRpcError":
      return new FoundgineRpcError(o.code, o.rpcMessage ?? o.message, o.data ?? undefined);
    case "FoundgineAbortError":
      return new FoundgineAbortError(o.message);
    case "FoundgineWatchTimeoutError":
      return new FoundgineWatchTimeoutError(o.lastStatus ?? undefined, o.message);
    default:
      return new FoundgineError(o.message);
  }
}

/** Anything thrown across the wasm boundary (a JSON string) or by user code becomes an Error. */
export function toError(thrown) {
  if (thrown instanceof Error) return thrown;
  if (typeof thrown === "string") {
    try {
      const parsed = JSON.parse(thrown);
      if (parsed && typeof parsed === "object" && typeof parsed.name === "string") return errorFromObject(parsed);
    } catch {
      /* not JSON: fall through */
    }
    return new FoundgineError(thrown);
  }
  return new FoundgineError(String(thrown));
}

/** The plain object the core understands for a caught error (used to feed `observeError`). */
export function errorToObject(error) {
  const o = { name: error.name, message: error.message };
  if (typeof error.status === "number") o.status = error.status;
  if (error.body !== undefined) o.body = error.body;
  return o;
}
