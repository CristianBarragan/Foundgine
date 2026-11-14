// Loading the WebAssembly module, plus the small host-side helpers every layer shares:
// `fetch` injection, per-request token resolution, abortable sleeping.

import wasmInit, * as wasm from "../pkg/foundgine_sdk_wasm.js";
import { FoundgineAbortError, FoundgineError, toError } from "./errors.js";

let ready = null;

/**
 * Load and instantiate the WebAssembly module once. Call it before constructing a client.
 * `input` may be bytes (`BufferSource`), a `WebAssembly.Module`, a `Response`/`Promise<Response>` or a URL.
 * With no argument, Node reads the `.wasm` file next to the package and browsers fetch it relative to this module.
 */
export function init(input) {
  if (!ready) {
    ready = (async () => {
      let source = input;
      if (source === undefined && typeof process !== "undefined" && process.versions?.node) {
        const { readFile } = await import(/* @vite-ignore */ "node:fs/promises");
        source = await readFile(new URL("../pkg/foundgine_sdk_wasm_bg.wasm", import.meta.url));
      }
      await wasmInit(source);
    })();
    ready.catch(() => {
      ready = null; // a failed load can be retried
    });
  }
  return ready;
}

export function core() {
  if (!ready) throw new FoundgineError("Call `await init()` before creating a client");
  return wasm;
}

/** Run a core call; anything it throws (a JSON string) becomes the matching `FoundgineError` subclass. */
export function call(fn) {
  try {
    return fn();
  } catch (e) {
    throw toError(e);
  }
}

export function resolveFetch(option) {
  const f = option ?? globalThis.fetch;
  if (typeof f !== "function") throw new FoundgineError("No fetch available: pass `fetch` in the client options");
  return option ? option : f.bind(globalThis);
}

/** Token option: a string, or a (possibly async) function called once per request. A blank token means none. */
export async function resolveToken(token) {
  const value = typeof token === "function" ? await token() : token;
  return core().normalizeToken(value ?? undefined);
}

export function isAbortLike(e, signal) {
  return Boolean(signal?.aborted) || e?.name === "AbortError" || e instanceof FoundgineAbortError;
}

export function throwIfAborted(signal) {
  if (signal?.aborted) throw new FoundgineAbortError();
}

/** Resolve after `ms`, or reject with `FoundgineAbortError` as soon as `signal` aborts. */
export function sleep(ms, signal) {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) return reject(new FoundgineAbortError());
    const onAbort = () => {
      clearTimeout(timer);
      reject(new FoundgineAbortError());
    };
    const timer = setTimeout(() => {
      signal?.removeEventListener("abort", onAbort);
      resolve();
    }, ms);
    signal?.addEventListener("abort", onAbort, { once: true });
  });
}

/** Wait for `promise`, but give up with `FoundgineAbortError` when `signal` aborts (the promise keeps running). */
export function raceAbort(promise, signal) {
  if (!signal) return promise;
  if (signal.aborted) return Promise.reject(new FoundgineAbortError());
  return new Promise((resolve, reject) => {
    const onAbort = () => reject(new FoundgineAbortError());
    signal.addEventListener("abort", onAbort, { once: true });
    promise.then(
      (v) => {
        signal.removeEventListener("abort", onAbort);
        resolve(v);
      },
      (e) => {
        signal.removeEventListener("abort", onAbort);
        reject(e);
      },
    );
  });
}

export function headersToPairs(headers) {
  const pairs = [];
  headers.forEach((value, key) => pairs.push([key, value]));
  return pairs;
}

/**
 * Send one request the core built and return the `{status, headers, body}` it understands (body read as text).
 * A caller abort becomes `FoundgineAbortError`; other network failures propagate unchanged, as with the TS SDK.
 */
export async function send(fetchImpl, request, signal) {
  throwIfAborted(signal);
  const init = { method: request.method, headers: Object.fromEntries(request.headers), signal };
  if (request.body !== null && request.body !== undefined) init.body = request.body;
  try {
    const res = await fetchImpl(request.url, init);
    const body = await res.text();
    return { status: res.status, headers: headersToPairs(res.headers), body };
  } catch (e) {
    if (isAbortLike(e, signal)) throw new FoundgineAbortError();
    throw e;
  }
}

export const nowMs = () => performance.now();
