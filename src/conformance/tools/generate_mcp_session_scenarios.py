#!/usr/bin/env python3
"""Generates mcp-session-scenarios.json (spec: mcp-session-lifecycle-v1).

Independent reference model of the MCP session manager, run by both runtimes' conformance tests. Session ids are random, so
steps use symbolic names; "session" may also be a literal id (malformed/unknown). Time is a fake clock starting at START.
Never edit the JSON by hand; change this generator and rerun it.
"""
import json, os

START = 1_000_000
MIN = 60_000
UNKNOWN = "A" * 43          # well formed, never issued
MALFORMED = "not a session id"


class Model:
    def __init__(self, idle, life, cap):
        self.idle, self.life, self.cap = idle, life, cap
        self.now, self.s = START, {}

    def exp(self, r): return min(r["idle"], r["abs"])

    def purge(self):
        dead = [k for k, r in self.s.items() if self.exp(r) <= self.now]
        for k in dead: del self.s[k]
        return len(dead)

    def run(self, st):
        op = st["op"]
        if op == "advance":
            self.now += st["ms"]; return st
        if op == "create":
            if len(self.s) >= self.cap: self.purge()
            if len(self.s) >= self.cap: return {**st, "expect": "full"}
            a = self.now + self.life
            self.s[st["as"]] = {"owner": st.get("owner"), "idle": min(self.now + self.idle, a), "abs": a}
            return {**st, "expect": "ok", "expiresAt": min(self.now + self.idle, a)}
        if op == "validate":
            r = self.s.get(st["session"])
            owner = st.get("owner")
            if r is None: return {**st, "expect": "NOT_FOUND"}
            if r["owner"] is not None and r["owner"] != owner: return {**st, "expect": "OWNER_MISMATCH"}
            if self.now >= self.exp(r):
                del self.s[st["session"]]; return {**st, "expect": "EXPIRED"}
            r["idle"] = max(r["idle"], min(self.now + self.idle, r["abs"]))
            return {**st, "expect": "VALID", "expiresAt": self.exp(r)}
        if op == "terminate":
            r = self.s.get(st["session"])
            if r is None or (r["owner"] is not None and r["owner"] != st.get("owner")): return {**st, "expect": False}
            live = self.now < self.exp(r)
            del self.s[st["session"]]
            return {**st, "expect": live}
        if op == "purge": return {**st, "expect": self.purge()}
        if op == "count": return {**st, "expect": len(self.s)}
        raise ValueError(op)


def scenario(name, idle, life, cap, steps):
    m = Model(idle, life, cap)
    return {"name": name, "startUnixMs": START, "idleTimeoutMs": idle, "maximumLifetimeMs": life,
            "maximumActiveSessions": cap, "steps": [m.run(s) for s in steps]}


def v(session, **kw): return {"op": "validate", "session": session, **kw}
def adv(ms): return {"op": "advance", "ms": ms}
def create(name, **kw): return {"op": "create", "as": name, **kw}
def term(session, **kw): return {"op": "terminate", "session": session, **kw}


S = [
    scenario("idle expiry slides on use", 10 * MIN, 25 * MIN, 10,
             [create("a"), adv(9 * MIN), v("a"), adv(9 * MIN), v("a"), adv(10 * MIN), v("a"), v("a")]),
    scenario("absolute lifetime never slides", 10 * MIN, 25 * MIN, 10,
             [create("a"), adv(9 * MIN), v("a"), adv(9 * MIN), v("a"), adv(5 * MIN), v("a"), adv(2 * MIN), v("a"), v("a")]),
    scenario("owner binding", 10 * MIN, 25 * MIN, 10,
             [create("a", owner="alice"), v("a", owner="alice"), v("a", owner="bob"), v("a"),
              term("a", owner="bob"), term("a"), v("a", owner="alice"), term("a", owner="alice"), v("a", owner="alice")]),
    scenario("unbound session ignores owner", 10 * MIN, 25 * MIN, 10,
             [create("a"), v("a", owner="anyone"), term("a", owner="anyone"), v("a")]),
    scenario("termination is final and idempotent", 10 * MIN, 25 * MIN, 10,
             [create("a"), term("a"), term("a"), v("a"), {"op": "count"}]),
    scenario("terminating an expired session reports false", 10 * MIN, 25 * MIN, 10,
             [create("a"), adv(10 * MIN), term("a"), v("a")]),
    scenario("bad ids are not found", 10 * MIN, 25 * MIN, 10,
             [create("a"), v(UNKNOWN), v(MALFORMED), v(""), term(UNKNOWN), term(MALFORMED), v("a")]),
    scenario("capacity, purge and reuse", 10 * MIN, 25 * MIN, 2,
             [create("a"), create("b"), create("c"), {"op": "count"}, term("a"), create("c"), adv(10 * MIN),
              {"op": "count"}, create("d"), {"op": "count"}, {"op": "purge"}, {"op": "count"}]),
    scenario("purge removes only expired", 10 * MIN, 25 * MIN, 10,
             [create("a"), adv(6 * MIN), create("b"), adv(5 * MIN), {"op": "purge"}, v("a"), v("b"), {"op": "count"}]),
    scenario("owner mismatch does not evict an expired session", 10 * MIN, 25 * MIN, 10,
             [create("a", owner="alice"), adv(10 * MIN), v("a", owner="bob"), {"op": "count"}, v("a", owner="alice"), {"op": "count"}]),
]

doc = {"spec": "mcp-session-lifecycle-v1", "scenarios": S}
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mcp-session-scenarios.json")
with open(out, "w") as f:
    json.dump(doc, f, indent=2); f.write("\n")
print("wrote", len(S), "scenarios")
