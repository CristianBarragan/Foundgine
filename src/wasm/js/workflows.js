// Temporal workflow client. Routes, parsing, the terminal-status set and the polling decisions are in the Rust
// core; this class does the host side: `fetch`, tokens, sleeping, callbacks, update history and cancellation.

import { errorFromObject, errorToObject, FoundgineAbortError, toError } from "./errors.js";
import { call, core, nowMs, resolveToken, send, sleep, throwIfAborted } from "./runtime.js";

export class WorkflowClient {
  #routes;
  #fetch;
  #token;

  /** @param {{ baseUrl: string, fetch: Function, token?: unknown }} o `baseUrl` must already be normalized. */
  constructor(o) {
    this.#routes = new (core().WorkflowRoutesCore)(o.baseUrl);
    this.#fetch = o.fetch;
    this.#token = o.token;
  }

  async #run(build, signal) {
    throwIfAborted(signal);
    const token = await resolveToken(this.#token);
    return send(this.#fetch, JSON.parse(build(token)), signal);
  }

  async #object(build, signal) {
    const response = await this.#run(build, signal);
    return call(() => core().parseObjectResponse(JSON.stringify(response)));
  }

  /** Names of the processes the server offers. */
  async processes(options = {}) {
    const raw = await this.#object((t) => this.#routes.processesRequest(t), options.signal);
    return JSON.parse(call(() => core().parseProcesses(raw)));
  }

  /** `{ name, steps: [{ name, requiresApproval, hasCompensation, approvalTimeoutSeconds }] }`. */
  async describe(process, options = {}) {
    const raw = await this.#object((t) => this.#routes.describeRequest(process, t), options.signal);
    return JSON.parse(call(() => core().parseDescription(raw)));
  }

  /** Start a workflow. `payload` defaults to `{}`. */
  async start(process, businessKey, payload, options = {}) {
    const body = payload === undefined ? undefined : JSON.stringify(payload);
    const raw = await this.#object((t) => call(() => this.#routes.startRequest(process, businessKey, body, t)), options.signal);
    return JSON.parse(call(() => core().parseStart(raw)));
  }

  /** `{ workflowId, temporalStatus, process?, raw, isFinished }`. */
  async status(process, businessKey, options = {}) {
    const raw = await this.#object((t) => this.#routes.statusRequest(process, businessKey, t), options.signal);
    return JSON.parse(call(() => core().parseStatus(raw)));
  }

  /**
   * Approve or reject a step. There is deliberately no caller-identity argument: the server takes the decision
   * subject from its own security context.
   */
  async decide(process, businessKey, step, approved, options = {}) {
    const response = await this.#run((t) => this.#routes.decisionRequest(process, businessKey, step, approved, options.reason, t), options.signal);
    call(() => core().parseEmptyResponse(JSON.stringify(response)));
  }

  async cancel(process, businessKey, options = {}) {
    const response = await this.#run((t) => this.#routes.cancelRequest(process, businessKey, t), options.signal);
    call(() => core().parseEmptyResponse(JSON.stringify(response)));
  }

  /**
   * Poll until the workflow reaches a terminal status. Returns at once with a `WorkflowWatch`.
   * Options: `intervalMs`, `timeoutMs`, `notFoundGraceMs`, `maxConsecutiveErrors`, `onUpdate(status)`,
   * `onError(error, consecutiveErrors)` (callback failures are swallowed) and `signal`.
   */
  watch(process, businessKey, options = {}) {
    return new WorkflowWatch((signal) => this.status(process, businessKey, { signal }), options);
  }

  /** Poll until finished, then return the final status. Any error ends the wait; a timeout throws `FoundgineWatchTimeoutError`. */
  async waitForCompletion(process, businessKey, options = {}) {
    const { signal } = options;
    const poller = new (core().WaitPollerCore)(options.intervalMs, options.timeoutMs, nowMs());
    try {
      for (;;) {
        const raw = await this.#object((t) => this.#routes.statusRequest(process, businessKey, t), signal);
        const status = JSON.parse(call(() => core().parseStatus(raw)));
        const step = JSON.parse(call(() => poller.observe(raw, nowMs())));
        if (step.kind === "finished") return status;
        if (step.kind === "fail") throw errorFromObject(step.error);
        await sleep(step.ms, signal);
      }
    } finally {
      poller.free();
    }
  }

  /** Start, then watch. Returns `{ start, watch }`. */
  async startAndWatch(process, businessKey, payload, options = {}) {
    const start = await this.start(process, businessKey, payload, { signal: options.signal });
    return { start, watch: this.watch(process, businessKey, options) };
  }
}

/** A running `watch()`. `done` settles with the final status; `updates()` replays history then follows live. */
export class WorkflowWatch {
  #history = [];
  #wake = new Set();
  #settled = false;
  #failure;
  #control = new AbortController();
  done;

  constructor(fetchStatus, options) {
    const { signal, onUpdate, onError } = options;
    if (signal) {
      if (signal.aborted) this.#control.abort();
      else signal.addEventListener("abort", () => this.#control.abort(), { once: true });
    }
    this.done = this.#poll(fetchStatus, options, onUpdate, onError);
    // Nobody is forced to await `done`: stop an unobserved failure from becoming an unhandled rejection.
    this.done.then(
      () => this.#finish(),
      (e) => this.#finish(e),
    );
  }

  /** The most recent distinct status seen so far. */
  get latest() {
    return this.#history[this.#history.length - 1];
  }

  /** Stop polling. `done` rejects with `FoundgineAbortError`. */
  stop() {
    this.#control.abort();
  }

  /** Every distinct status, from the first one (history is replayed), ending when the watch finishes. Throws if it failed. */
  async *updates() {
    let i = 0;
    for (;;) {
      while (i < this.#history.length) yield this.#history[i++];
      if (this.#settled) {
        if (this.#failure) throw this.#failure;
        return;
      }
      await new Promise((resolve) => this.#wake.add(resolve));
    }
  }

  #notify() {
    const waiting = [...this.#wake];
    this.#wake.clear();
    for (const wake of waiting) wake();
  }

  #finish(error) {
    this.#settled = true;
    this.#failure = error;
    this.#notify();
  }

  async #poll(fetchStatus, options, onUpdate, onError) {
    const signal = this.#control.signal;
    const poller = new (core().WatchPollerCore)(options.intervalMs, options.timeoutMs, options.notFoundGraceMs, options.maxConsecutiveErrors, nowMs());
    try {
      for (;;) {
        throwIfAborted(signal);
        let step;
        let failure;
        try {
          const status = await fetchStatus(signal);
          const observation = JSON.parse(call(() => poller.observeStatus(JSON.stringify(status.raw), nowMs())));
          if (observation.isNew) {
            this.#history.push(status);
            this.#notify();
            if (onUpdate) {
              try {
                await onUpdate(status);
              } catch {
                /* callback failures are swallowed */
              }
            }
          }
          step = observation.step;
        } catch (thrown) {
          const error = toError(thrown);
          if (error instanceof FoundgineAbortError) throw error;
          const observation = JSON.parse(call(() => poller.observeError(JSON.stringify(errorToObject(error)), nowMs())));
          if (observation.notifyError !== undefined && onError) {
            try {
              await onError(error, observation.notifyError);
            } catch {
              /* callback failures are swallowed */
            }
          }
          step = observation.step;
          failure = error;
        }
        if (step.kind === "finished") return this.latest;
        if (step.kind === "fail") throw failure ?? errorFromObject(step.error);
        await sleep(step.ms, signal);
      }
    } finally {
      poller.free();
    }
  }
}
