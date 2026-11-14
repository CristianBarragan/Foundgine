/** One dispatched Server-Sent Event. Fields are absent when the block omitted them. */
export interface SseEvent { readonly id?: string; readonly event?: string; readonly data: string }

/** Incremental WHATWG-compatible SSE parser. Feed arbitrary text or UTF-8 byte chunks. */
export class SseParser {
  #buffer = "";
  #decoder = new TextDecoder("utf-8", { fatal: true });
  #started = false;
  #skipLf = false;
  #data: string[] = [];
  #id: string | undefined;
  #event: string | undefined;
  lastEventId: string | undefined;
  retryMs: number | undefined;

  push(chunk: string | Uint8Array): SseEvent[] {
    let text = typeof chunk === "string" ? chunk : this.#decoder.decode(chunk, { stream: true });
    if (!this.#started && text.length > 0) {
      this.#started = true;
      if (text.startsWith("\uFEFF")) text = text.slice(1);
    }
    this.#buffer += text;
    return this.#drain();
  }

  #drain(): SseEvent[] {
    const events: SseEvent[] = [];
    let pos = 0;
    const input = this.#buffer;
    while (pos < input.length) {
      if (this.#skipLf) {
        this.#skipLf = false;
        if (input[pos] === "\n") { pos++; continue; }
      }
      let end = pos;
      while (end < input.length && input[end] !== "\r" && input[end] !== "\n") end++;
      if (end === input.length) break;
      if (input[end] === "\r") this.#skipLf = true;
      const event = this.#line(input.slice(pos, end));
      if (event) events.push(event);
      pos = end + 1;
    }
    this.#buffer = input.slice(pos);
    return events;
  }

  #line(line: string): SseEvent | undefined {
    if (line === "") return this.#dispatch();
    if (line.startsWith(":")) return undefined;
    const colon = line.indexOf(":");
    const name = colon < 0 ? line : line.slice(0, colon);
    let value = colon < 0 ? "" : line.slice(colon + 1);
    if (value.startsWith(" ")) value = value.slice(1);
    switch (name) {
      case "data": this.#data.push(value); break;
      case "id": if (!value.includes("\0")) this.#id = value; break;
      case "event": this.#event = value; break;
      case "retry": if (/^[0-9]+$/.test(value)) this.retryMs = Number(value); break;
    }
    return undefined;
  }

  #dispatch(): SseEvent | undefined {
    const id = this.#id;
    const event = this.#event;
    const data = this.#data;
    this.#id = undefined;
    this.#event = undefined;
    this.#data = [];
    if (id !== undefined) this.lastEventId = id;
    if (data.length === 0) return undefined;
    return { ...(id === undefined ? {} : { id }), ...(event === undefined ? {} : { event }), data: data.join("\n") };
  }
}
