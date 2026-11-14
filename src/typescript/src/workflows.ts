import { FoundgineAbortError, FoundgineError, FoundgineHttpError, FoundgineWatchTimeoutError } from "./errors.js";
import { encodePathSegment, ensureSuccess, joinUrl, resolveToken, type FetchLike, type TokenProvider } from "./http.js";

export interface WorkflowStartResult {
  readonly workflowId: string;
  readonly status: string;
  readonly alreadyExisted: boolean;
  readonly raw: Record<string, unknown>;
}
export interface WorkflowStatus {
  readonly workflowId: string;
  readonly temporalStatus: string;
  readonly process: unknown;
  readonly raw: Record<string, unknown>;
}
export interface WorkflowStepDescription {
  readonly name: string;
  readonly requiresApproval: boolean;
  readonly hasCompensation: boolean;
  readonly approvalTimeoutSeconds: number | null;
}
export interface WorkflowProcessDescription {
  readonly name: string;
  readonly steps: readonly WorkflowStepDescription[];
}
export interface WorkflowWatchOptions {
  readonly intervalMs?: number;
  readonly timeoutMs?: number;
  readonly notFoundGraceMs?: number;
  readonly maxConsecutiveErrors?: number;
  readonly onUpdate?: (status: WorkflowStatus) => void | Promise<void>;
  readonly onError?: (error: unknown, consecutiveCount: number) => void | Promise<void>;
  readonly signal?: AbortSignal;
}
export interface WorkflowWatch {
  readonly done: Promise<WorkflowStatus>;
  readonly isFinished: boolean;
  readonly latest: WorkflowStatus | undefined;
  stop(): void;
  updates(): AsyncGenerator<WorkflowStatus>;
}

const terminal = new Set(["COMPLETED", "FAILED", "CANCELED", "CANCELLED", "TERMINATED", "TIMED_OUT"]);
const sleep = (ms: number, signal: AbortSignal): Promise<void> => new Promise((resolve, reject) => {
  if (signal.aborted) { reject(new FoundgineAbortError()); return; }
  const timer = setTimeout(done, ms);
  function done() { signal.removeEventListener("abort", abort); resolve(); }
  function abort() { clearTimeout(timer); signal.removeEventListener("abort", abort); reject(new FoundgineAbortError()); }
  signal.addEventListener("abort", abort, { once: true });
});

/** Authenticated HTTP client for Foundgine's Temporal workflow routes. */
export class WorkflowClient {
  readonly #baseUrl: string;
  readonly #fetch: FetchLike;
  readonly #token: TokenProvider;
  constructor(baseUrl: string, fetch: FetchLike, token?: TokenProvider) {
    this.#baseUrl = baseUrl;
    this.#fetch = fetch;
    this.#token = token;
  }

  /** List registered process names for the authenticated tenant. */
  async processes(): Promise<string[]> {
    const raw = await this.#request("GET", "", undefined, false);
    if (!Array.isArray(raw.processes) || raw.processes.some((name) => typeof name !== "string")) {
      throw new FoundgineError("Workflow catalog response is missing processes");
    }
    return raw.processes as string[];
  }

  /** Describe public process metadata; operation identifiers are never returned by the server. */
  async describe(process: string): Promise<WorkflowProcessDescription> {
    const raw = await this.#request("GET", `/${encodePathSegment(process)}`);
    if (typeof raw.name !== "string" || !Array.isArray(raw.steps)) throw new FoundgineError("Workflow process description is invalid");
    const steps = raw.steps.map((value): WorkflowStepDescription => {
      if (!value || typeof value !== "object" || Array.isArray(value)) throw new FoundgineError("Workflow step description is invalid");
      const step = value as Record<string, unknown>;
      if (typeof step.name !== "string") throw new FoundgineError("Workflow step description is missing name");
      return { name: step.name, requiresApproval: step.requiresApproval === true, hasCompensation: step.hasCompensation === true, approvalTimeoutSeconds: typeof step.approvalTimeoutSeconds === "number" ? step.approvalTimeoutSeconds : null };
    });
    return { name: raw.name, steps };
  }

  async start(process: string, businessKey: string, payload: unknown = {}): Promise<WorkflowStartResult> {
    const raw = await this.#request("POST", `/${encodePathSegment(process)}`, { businessKey, payload });
    if (typeof raw.workflowId !== "string") throw new FoundgineError("Workflow start returned no workflowId");
    return { workflowId: raw.workflowId, status: typeof raw.status === "string" ? raw.status : "Started", alreadyExisted: raw.alreadyExisted === true, raw };
  }

  async status(process: string, businessKey: string): Promise<WorkflowStatus> {
    return this.#status(process, businessKey);
  }

  async #status(process: string, businessKey: string, signal?: AbortSignal): Promise<WorkflowStatus> {
    const raw = await this.#request("GET", `/${encodePathSegment(process)}/${encodePathSegment(businessKey)}`, undefined, false, signal);
    if (typeof raw.workflowId !== "string" || typeof raw.temporalStatus !== "string") throw new FoundgineError("Workflow status response is missing workflowId or temporalStatus");
    return { workflowId: raw.workflowId, temporalStatus: raw.temporalStatus, process: raw.process, raw };
  }

  async decide(process: string, businessKey: string, step: string, approved: boolean, reason?: string): Promise<void> {
    const body: Record<string, unknown> = { step, approved };
    if (reason !== undefined) body.reason = reason;
    await this.#request("POST", `/${encodePathSegment(process)}/${encodePathSegment(businessKey)}/decision`, body, true);
  }

  async cancel(process: string, businessKey: string): Promise<void> {
    await this.#request("POST", `/${encodePathSegment(process)}/${encodePathSegment(businessKey)}/cancel`, undefined, true);
  }

  async waitForCompletion(process: string, businessKey: string, options: Pick<WorkflowWatchOptions, "intervalMs" | "timeoutMs" | "signal"> = {}): Promise<WorkflowStatus> {
    const controller = new AbortController();
    const relay = () => controller.abort();
    options.signal?.addEventListener("abort", relay, { once: true });
    const started = Date.now();
    let last: WorkflowStatus | undefined;
    try {
      while (true) {
        if (controller.signal.aborted) throw new FoundgineAbortError();
        last = await this.#status(process, businessKey, controller.signal);
        if (isFinished(last)) return last;
        if (options.timeoutMs !== undefined && Date.now() - started >= options.timeoutMs) throw new FoundgineWatchTimeoutError(last);
        await sleep(options.intervalMs ?? 1000, controller.signal);
      }
    } finally { options.signal?.removeEventListener("abort", relay); }
  }

  watch(process: string, businessKey: string, options: WorkflowWatchOptions = {}): WorkflowWatch {
    const controller = new AbortController();
    let latest: WorkflowStatus | undefined;
    let finished = false;
    let stopped = false;
    const history: WorkflowStatus[] = [];
    const waiters = new Set<() => void>();
    const notify = () => { for (const wake of waiters) wake(); waiters.clear(); };
    const push = (status: WorkflowStatus): boolean => {
      const previous = history.at(-1);
      if (previous && JSON.stringify(previous.raw) === JSON.stringify(status.raw)) return false;
      latest = status; history.push(status); notify();
      return true;
    };
    const relay = () => controller.abort();
    options.signal?.addEventListener("abort", relay, { once: true });
    const done = this.#poll(process, businessKey, options, controller.signal, push)
      .then((status) => { finished = true; notify(); return status; })
      .catch((error: unknown) => { finished = true; notify(); throw error; })
      .finally(() => options.signal?.removeEventListener("abort", relay));
    // Mark a rejection as observed even if a consumer only uses updates()/stop(). Awaiting done still sees the original rejection.
    void done.catch(() => undefined);
    return {
      done,
      get isFinished() { return finished; },
      get latest() { return latest; },
      stop() { if (!controller.signal.aborted) { stopped = true; controller.abort(); } },
      async *updates() {
        let index = 0;
        while (true) {
          while (index < history.length) yield history[index++]!;
          if (finished) {
            if (stopped || controller.signal.aborted) throw new FoundgineAbortError();
            return;
          }
          await new Promise<void>((resolve) => waiters.add(resolve));
        }
      },
    };
  }

  async startAndWatch(process: string, businessKey: string, payload: unknown = {}, options: WorkflowWatchOptions = {}): Promise<{ started: WorkflowStartResult; watch: WorkflowWatch }> {
    const started = await this.start(process, businessKey, payload);
    return { started, watch: this.watch(process, businessKey, options) };
  }

  async #poll(process: string, key: string, options: WorkflowWatchOptions, signal: AbortSignal, push: (s: WorkflowStatus) => boolean): Promise<WorkflowStatus> {
    const started = Date.now();
    const interval = options.intervalMs ?? 1000;
    let latest: WorkflowStatus | undefined;
    const maxErrors = options.maxConsecutiveErrors ?? 5;
    const grace = options.notFoundGraceMs ?? 5000;
    let errors = 0;
    while (true) {
      if (signal.aborted) throw new FoundgineAbortError();
      try {
        const status = await this.#status(process, key, signal);
        latest = status;
        errors = 0;
        if (push(status)) {
          try { await options.onUpdate?.(status); } catch { /* user callback errors must not kill polling */ }
        }
        if (isFinished(status)) return status;
      } catch (error) {
        if (error instanceof FoundgineHttpError && error.status === 401) throw error;
        if (error instanceof FoundgineHttpError && error.status === 404) {
          if (Date.now() - started > grace) throw error;
          // A just-started workflow may not be visible to the status endpoint immediately.
        } else {
          errors++;
          try { await options.onError?.(error, errors); } catch { /* callback errors are isolated */ }
          if (errors > maxErrors) throw error;
        }
      }
      if (options.timeoutMs !== undefined && Date.now() - started >= options.timeoutMs) throw new FoundgineWatchTimeoutError(latest);
      await sleep(interval, signal);
    }
  }

  async #request(method: string, path: string, body?: unknown, empty = false, signal?: AbortSignal): Promise<Record<string, unknown>> {
    const headers = new Headers({ Accept: "application/json" });
    const token = await resolveToken(this.#token);
    if (token) headers.set("Authorization", `Bearer ${token}`);
    if (body !== undefined) headers.set("Content-Type", "application/json");
    let response: Response;
    try {
      response = await this.#fetch(joinUrl(this.#baseUrl, `/foundgine/workflows${path}`), {
        method, headers, ...(body !== undefined ? { body: JSON.stringify(body) } : {}), ...(signal ? { signal } : {}),
      });
    } catch (error) {
      if (signal?.aborted) throw new FoundgineAbortError();
      throw error;
    }
    await ensureSuccess(response);
    if (empty || response.status === 204) return {};
    const text = await response.text();
    if (!text.trim()) return {};
    let value: unknown;
    try { value = JSON.parse(text) as unknown; }
    catch { throw new FoundgineError("Workflow endpoint returned invalid JSON"); }
    if (!value || typeof value !== "object" || Array.isArray(value)) throw new FoundgineError("Workflow endpoint returned a non-object JSON response");
    return value as Record<string, unknown>;
  }
}

function isFinished(status: WorkflowStatus): boolean {
  return terminal.has(status.temporalStatus.toUpperCase());
}
