from __future__ import annotations

import pytest

from foundgine import SseEvent, SseParser


def feed_chars(text: str) -> list[SseEvent]:
    p = SseParser()
    out: list[SseEvent] = []
    for ch in text:
        out += p.push(ch)
    return out


def test_split_chunks_crlf_comments_and_multiline_data() -> None:
    # Same case as the TS test "SseParser handles split chunks, CRLF, comments and multi-line data".
    p = SseParser()
    out = [*p.push(": hb\r\nid: 7\r\nda"), *p.push("ta: a\r\ndata: b\r\n\r\n")]
    assert out == [SseEvent(id="7", event=None, data="a\nb")]


@pytest.mark.parametrize("eol", ["\n", "\r\n", "\r"])
def test_every_line_ending_works_whole_and_one_char_at_a_time(eol: str) -> None:
    frame = f"id: 1{eol}data: x{eol}data: y{eol}{eol}"
    want = [SseEvent("1", None, "x\ny")]
    assert SseParser().push(frame) == want
    assert feed_chars(frame) == want


def test_lone_cr_split_from_its_lf_is_one_line_break() -> None:
    p = SseParser()
    got = [*p.push("data: a\r"), *p.push("\n"), *p.push("\r\n")]
    assert got == [SseEvent(None, None, "a")]  # one blank line, not two


def test_cr_then_cr_is_two_breaks() -> None:
    assert SseParser().push("data: a\r\rdata: b\r\r") == [SseEvent(None, None, "a"), SseEvent(None, None, "b")]


def test_unicode_separators_are_payload_not_line_breaks() -> None:
    # str.splitlines() would split on every one of these. The server sends them raw.
    nasty = "a\u2028b\u2029c\x85d\x0be\x0cf\x1cg\x1dh\x1ei"
    frame = f"id: 9\ndata: {nasty}\n\n"
    assert SseParser().push(frame) == [SseEvent("9", None, nasty)]
    assert feed_chars(frame) == [SseEvent("9", None, nasty)]


def test_only_one_space_after_the_colon_is_removed() -> None:
    assert SseParser().push("data:  two spaces\n\n")[0].data == " two spaces"
    assert SseParser().push("data:none\n\n")[0].data == "none"
    assert SseParser().push("data: \u00a0nbsp kept\n\n")[0].data == "\u00a0nbsp kept"


def test_field_without_colon_and_empty_data_line() -> None:
    assert SseParser().push("data\ndata\n\n") == [SseEvent(None, None, "\n")]
    assert SseParser().push("data:\n\n") == [SseEvent(None, None, "")]


def test_injected_field_text_inside_a_payload_stays_payload() -> None:
    # The server escapes by line: every payload line is its own `data:` line.
    frame = "id: 3\ndata: id: 99\ndata: event: evil\n\n"
    [e] = SseParser().push(frame)
    assert (e.id, e.event, e.data) == ("3", None, "id: 99\nevent: evil")


def test_event_field_sets_event_and_comments_are_ignored() -> None:
    assert SseParser().push(": hb\nevent: ping\ndata: {}\n\n") == [SseEvent(None, "ping", "{}")]


def test_block_without_data_is_not_an_event_but_its_id_is_remembered() -> None:
    p = SseParser()
    assert p.push("id: 5\n\n") == []
    assert p.last_event_id == "5"
    assert p.push("data: x\n\n") == [SseEvent(None, None, "x")]  # id is per event; last_event_id keeps the sticky value
    assert p.last_event_id == "5"


def test_id_with_nul_is_ignored() -> None:
    assert SseParser().push("id: a\x00b\ndata: x\n\n") == [SseEvent(None, None, "x")]


def test_retry_only_accepts_ascii_digits() -> None:
    p = SseParser()
    p.push("retry: 1500\n")
    assert p.retry_ms == 1500
    p.push("retry: 12x\nretry: \u0663\n")  # not digits per the spec (Arabic-Indic digit three)
    assert p.retry_ms == 1500


def test_incomplete_block_waits_for_more_input() -> None:
    p = SseParser()
    assert p.push("id: 1\ndata: par") == []
    assert p.push("tial\n") == []
    assert p.push("\n") == [SseEvent("1", None, "partial")]


def test_bytes_with_a_multibyte_character_split_across_chunks() -> None:
    frame = "id: 1\ndata: h\u00e9llo \U0001f600 \u4e16\u754c\n\n".encode()
    p = SseParser()
    got: list[SseEvent] = []
    for i in range(len(frame)):  # one byte at a time splits every multi-byte character
        got += p.push(frame[i : i + 1])
    assert got == [SseEvent("1", None, "h\u00e9llo \U0001f600 \u4e16\u754c")]


def test_leading_bom_is_ignored_only_at_stream_start() -> None:
    assert SseParser().push(b"\xef\xbb\xbfdata: a\n\n") == [SseEvent(None, None, "a")]
    p = SseParser()
    p.push("data: a\n\n")
    assert p.push("\ufeffdata: b\n\n") == []  # a later BOM is part of the field name "\ufeffdata", so no data


def test_invalid_utf8_raises() -> None:
    with pytest.raises(UnicodeDecodeError):
        SseParser().push(b"data: \xff\n\n")


def test_many_events_in_one_chunk_keep_order() -> None:
    frames = "".join(f"id: {i}\ndata: m{i}\n\n" for i in range(1, 6))
    assert [(e.id, e.data) for e in SseParser().push(frames)] == [(str(i), f"m{i}") for i in range(1, 6)]
