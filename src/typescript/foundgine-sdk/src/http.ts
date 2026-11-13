import { FoundgineHttpError } from "./errors.js";

export type TokenProvider = string | (() => string | null | undefined | Promise<string | null | undefined>) | null | undefined;
export type FetchLike = typeof fetch;

export async function resolveToken(token: TokenProvider): Promise<string | undefined> {
  const value = typeof token === "function" ? await token() : token;
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

export function joinUrl(baseUrl: string, path: string): string {
  return `${baseUrl.replace(/\/+$/, "")}/${path.replace(/^\/+/, "")}`;
}

/** Encode one route segment using RFC 3986 unreserved characters only (Python/Rust parity). */
export function encodePathSegment(value: string): string {
  return encodeURIComponent(value).replace(/[!'()*]/g, (character) =>
    `%${character.charCodeAt(0).toString(16).toUpperCase()}`,
  );
}

export async function errorFor(response: Response): Promise<FoundgineHttpError> {
  const text = await response.text();
  let body: unknown;
  try { body = text ? JSON.parse(text) as unknown : undefined; } catch { body = undefined; }
  let detail: string | undefined;
  if (body && typeof body === "object") {
    const error = (body as { error?: unknown }).error;
    if (typeof error === "string") detail = error;
    else if (error && typeof error === "object" && typeof (error as { message?: unknown }).message === "string") {
      detail = (error as { message: string }).message;
    }
  }
  if (!detail && text && body === undefined) detail = text.slice(0, 200).trim() || undefined;
  return new FoundgineHttpError(response.status, detail ? `HTTP ${response.status}: ${detail}` : `HTTP ${response.status}`, body);
}

export async function ensureSuccess(response: Response): Promise<void> {
  if (!response.ok) throw await errorFor(response);
}
