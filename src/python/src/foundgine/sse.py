r"""Incremental Server-Sent Events parser.

Follows the WHATWG SSE rules, with the points REQUIREMENTS.md calls out:

* Lines end with \r\n, \r or \n and nothing else. str.splitlines() and a bare str.strip() are never used:
  they also act on U+000B, U+000C, U+001C-U+001E, U+0085, U+2028 and U+2029, which the server sends raw in payloads.
* A \r ends its line at once, so an event is never delayed waiting for a possible \n. If the next character is \n,
  even in a later chunk, it is skipped as the second half of \r\n.
* Only one space after the colon is removed.
* Bytes are decoded incrementally, so a multi-byte character split across chunks survives. A leading BOM is dropped.
"""

from __future__ import annotations

import codecs
from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class SseEvent:
    """One dispatched event. ``id`` and ``event`` are None unless the block carried that field."""

    id: str | None
    event: str | None
    data: str


class SseParser:
    """Feed it text or bytes with :meth:`push`; it returns the events completed by that chunk."""

    def __init__(self) -> None:
        self._buf = ""
        self._decoder = codecs.getincrementaldecoder("utf-8")("strict")
        self._started = False
        self._skip_lf = False
        self._data: list[str] = []
        self._id: str | None = None
        self._event: str | None = None
        self.last_event_id: str | None = None
        """Last ``id`` seen on any dispatched event (the value to send as ``Last-Event-ID``)."""
        self.retry_ms: int | None = None
        """Last valid ``retry:`` value, in milliseconds."""

    def push(self, chunk: str | bytes) -> list[SseEvent]:
        if isinstance(chunk, bytes | bytearray | memoryview):
            text = self._decoder.decode(bytes(chunk))
        else:
            text = chunk
        if not self._started and text:
            self._started = True
            text = text.removeprefix("\ufeff")
        self._buf += text
        return self._drain()

    def _drain(self) -> list[SseEvent]:
        out: list[SseEvent] = []
        buf, pos, n = self._buf, 0, len(self._buf)
        while pos < n:
            if self._skip_lf:
                self._skip_lf = False
                if buf[pos] == "\n":
                    pos += 1
                    continue
            end = pos
            while end < n and buf[end] != "\r" and buf[end] != "\n":
                end += 1
            if end == n:
                break  # no terminator yet: wait for more input
            if buf[end] == "\r":
                self._skip_lf = True  # a following "\n" (possibly in the next chunk) belongs to this CRLF
            event = self._line(buf[pos:end])
            if event is not None:
                out.append(event)
            pos = end + 1
        self._buf = buf[pos:]
        return out

    def _line(self, line: str) -> SseEvent | None:
        if line == "":
            return self._dispatch()
        if line[0] == ":":
            return None  # comment / heartbeat
        name, sep, value = line.partition(":")
        if sep and value[:1] == " ":
            value = value[1:]
        if name == "data":
            self._data.append(value)
        elif name == "id" and "\x00" not in value:
            self._id = value
        elif name == "event":
            self._event = value
        elif name == "retry" and value.isascii() and value.isdigit():
            self.retry_ms = int(value)
        return None

    def _dispatch(self) -> SseEvent | None:
        event_id, event_name, data = self._id, self._event, self._data
        self._id, self._event, self._data = None, None, []
        if event_id is not None:
            self.last_event_id = event_id
        if not data:
            return None  # a block with no data lines is not an event
        return SseEvent(id=event_id, event=event_name, data="\n".join(data))
