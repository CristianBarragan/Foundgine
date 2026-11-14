#!/usr/bin/env python3
"""Generates mcp-sse-scenarios.json (spec: mcp-sse-replay-v1).

Independent reference model of the MCP SSE replay buffer and frame format, run by both runtimes' conformance tests.
Never edit the JSON by hand; change this generator and rerun it.
"""
import json, os, re


class Model:
    def __init__(self, max_events, max_bytes):
        self.me, self.mb, self.ev, self.last = max_events, max_bytes, [], 0   # ev: list of (seq, data)
        self.sub = None   # name of the handle holding the single live-stream slot
        self.retired = False

    def size(self, d): return len(d.encode("utf-8"))

    def run(self, st):
        op = st["op"]
        if op == "append":
            if self.size(st["data"]) > self.mb: return {**st, "expect": "rejected", "woke": False}
            self.last += 1
            self.ev.append((self.last, st["data"]))
            while len(self.ev) > self.me or sum(self.size(d) for _, d in self.ev) > self.mb: self.ev.pop(0)
            return {**st, "expect": "ok", "id": str(self.last), "woke": self.sub is not None}
        if op == "replay":
            lid = st.get("lastEventId")
            s = None if lid is None else lid.strip(" \t")   # only space and tab: runtimes disagree on Unicode whitespace
            if not s: return {**st, "expect": "OK", "ids": []}
            if not (s.isascii() and s.isdigit()) or len(s) > 18: return {**st, "expect": "INVALID", "ids": []}
            after = int(s)
            if after > self.last: return {**st, "expect": "INVALID", "ids": []}
            oldest = self.ev[0][0] if self.ev else self.last + 1
            if after < oldest - 1: return {**st, "expect": "EVICTED", "ids": []}
            return {**st, "expect": "OK", "ids": [str(q) for q, _ in self.ev if q > after]}
        if op == "subscribe":
            if self.sub is not None or self.retired: return {**st, "expect": "busy"}
            self.sub = st["as"]
            return {**st, "expect": "ok"}
        if op == "unsubscribe":
            if self.sub == st["as"]: self.sub = None   # a stale or foreign handle never frees the slot
            return {**st, "expect": "done"}
        if op == "retire":
            told = [self.sub] if self.sub is not None else []
            self.sub, self.retired = None, True   # the slot is freed for good; retained events stay readable
            return {**st, "expect": "done", "retired": told}
        if op == "format":
            lines = re.split(r"\r\n|\n|\r", st["data"])
            frame = "id: %s\n" % st["sequence"] + "".join("data: %s\n" % l for l in lines) + "\n"
            return {**st, "expect": frame}
        raise ValueError(op)


def scenario(name, me, mb, steps):
    m = Model(me, mb)
    return {"name": name, "maxEvents": me, "maxBytes": mb, "steps": [m.run(s) for s in steps]}


def a(d): return {"op": "append", "data": d}
def r(lid=None): return {"op": "replay", **({} if lid is None else {"lastEventId": lid})}
def sub(n): return {"op": "subscribe", "as": n}
def unsub(n): return {"op": "unsubscribe", "as": n}
def retire(): return {"op": "retire"}
def f(seq, d): return {"op": "format", "sequence": seq, "data": d}


S = [
    scenario("ids start at one and replay is exclusive", 10, 10_000,
             [r(), a("a"), a("b"), a("c"), r(), r("0"), r("1"), r("2"), r("3"), r(" 2 ")]),
    scenario("event count eviction reports lost events", 3, 10_000,
             [a("1"), a("2"), a("3"), a("4"), a("5"), r("0"), r("1"), r("2"), r("3"), r("5")]),
    scenario("byte limit evicts oldest and refuses oversized events", 100, 10,
             [a("aaaa"), a("bbbb"), a("cc"), r("0"), a("dddd"), r("0"), r("1"), r("2"), a("x" * 11), a("é" * 5), a("é" * 6), r("3")]),
    scenario("unparseable and future ids are invalid", 5, 1000,
             [a("a"), r("abc"), r("-1"), r("1.5"), r("2"), r("99999999999999999999"), r("1e3"), r("١"), r("\u00a01"), r("\t1\t"), r("   "), r("1")]),
    scenario("sequence never rewinds after eviction", 1, 1000,
             [a("a"), a("b"), a("c"), r("2"), r("1"), r("3"), r("4")]),
    scenario("one live stream at a time and appends wake it", 5, 1000,
             [a("before"), sub("s1"), sub("s2"), a("x"), unsub("s2"), sub("s3"), a("y"), a("z" * 1001), unsub("s1"), a("quiet"),
              sub("s3"), a("loud"), unsub("s1"), sub("s4"), unsub("s3"), sub("s4"), a("again"), r("2")]),
    scenario("an empty buffer can be retired and still takes events", 5, 1000,
             [retire(), a("x"), r("0")]),
    scenario("retiring a buffer tells the holder once and refuses new streams", 5, 1000,
             [a("a"), sub("s1"), sub("s2"), retire(), a("b"), sub("s3"), unsub("s1"), retire(), r("0"), r("1")]),
    scenario("frame format keeps payload lines apart", 1, 1000,
             [f("7", "hello"), f("8", ""), f("9", "one\ntwo"), f("10", "a\r\nb\rc"), f("11", "x\n\nid: 999\nevent: evil"),
              f("12", "trailing\n"), f("13", "unicode é ✓")]),
]

doc = {"spec": "mcp-sse-replay-v1", "scenarios": S}
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mcp-sse-scenarios.json")
with open(out, "w") as fh:
    json.dump(doc, fh, indent=2, ensure_ascii=False); fh.write("\n")
print("wrote", len(S), "scenarios")
