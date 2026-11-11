#!/usr/bin/env python3
"""Generates conformance/semantic-query-vectors.json (spec: semantic-query-v1).

Independent reference for the C# and Java SemanticQuery builders: it interprets the same step lists and renders the
expected canonical text of the resulting operation. Never edit the JSON by hand; change this file and regenerate.
"""
import json, os

MODEL = {
    "entities": {
        "Warehouse": [["Id", "int"], ["Name", "string"], ["Region", "string"]],
        "InventoryLot": [["Id", "int"], ["Sku", "string"], ["OnHand", "int"]],
    },
    "relationships": [{"name": "Lots", "source": "Warehouse", "target": "InventoryLot"}],
}

CASES = [
    {"name": "default_selects_all_fields", "entity": "Warehouse", "steps": []},
    {"name": "select_by_name_case_insensitive", "entity": "Warehouse",
     "steps": [{"op": "select", "names": ["id", "NAME"]}]},
    {"name": "where_eq_single_filter_is_not_wrapped", "entity": "Warehouse",
     "steps": [{"op": "whereEq", "field": "Region", "value": "NZ"}]},
    {"name": "several_conditions_are_anded", "entity": "Warehouse",
     "steps": [{"op": "whereEq", "field": "Region", "value": "NZ"}, {"op": "whereNeq", "field": "Name", "value": "Depot"},
               {"op": "whereIn", "field": "Id", "values": [1, 2, 3]}]},
    {"name": "order_take_skip", "entity": "Warehouse",
     "steps": [{"op": "orderBy", "field": "Name", "dir": "desc"}, {"op": "orderBy", "field": "Id", "dir": "asc"},
               {"op": "take", "n": 5}, {"op": "skip", "n": 10}]},
    {"name": "include_infers_target_and_all_fields", "entity": "Warehouse",
     "steps": [{"op": "include", "relationship": "Lots", "steps": []}]},
    {"name": "include_with_refinement", "entity": "Warehouse",
     "steps": [{"op": "select", "names": ["Id"]}, {"op": "whereEq", "field": "Region", "value": "NZ"},
               {"op": "include", "relationship": "Lots",
                "steps": [{"op": "select", "names": ["Sku", "OnHand"]}, {"op": "whereNeq", "field": "OnHand", "value": 0},
                          {"op": "orderBy", "field": "Sku", "dir": "asc"}, {"op": "take", "n": 50}]}]},
    {"name": "error_unknown_entity", "entity": "Nope", "steps": []},
    {"name": "error_unknown_field", "entity": "Warehouse", "steps": [{"op": "select", "names": ["Nope"]}]},
]

def value(v):
    return json.dumps(v) if isinstance(v, str) else str(v)

def expected(case):
    if case["entity"] not in MODEL["entities"]:
        return {"error": "Unknown entity " + case["entity"]}
    try:
        return {"rendered": render_node([1], case["entity"], case["steps"], None)}
    except KeyError as e:
        return {"error": e.args[0]}

def render_node(counter, entity, steps, via):
    node_id = counter[0]; counter[0] += 1
    fields = [f for f, _ in MODEL["entities"][entity]]
    lower = {f.lower(): f for f in fields}
    def field(n):
        if n.lower() not in lower: raise KeyError("Unknown field %s.%s" % (entity, n))
        return lower[n.lower()]
    selected, filters, order, limit, offset, children = None, [], [], None, None, []
    for st in steps:
        op = st["op"]
        if op == "select": selected = [field(n) for n in st["names"]]
        elif op == "whereEq": filters.append("%s eq %s" % (field(st["field"]), value(st["value"])))
        elif op == "whereNeq": filters.append("%s neq %s" % (field(st["field"]), value(st["value"])))
        elif op == "whereIn": filters.append("%s in [%s]" % (field(st["field"]), ", ".join(value(v) for v in st["values"])))
        elif op == "orderBy": order.append("%s %s" % (field(st["field"]), st["dir"]))
        elif op == "take": limit = st["n"]
        elif op == "skip": offset = st["n"]
        elif op == "include":
            rel = next(r for r in MODEL["relationships"] if r["source"] == entity and r["name"] == st["relationship"])
            children.append(render_node(counter, rel["target"], st["steps"], rel["name"]))
    out = "#%d %s(%s)" % (node_id, entity, ", ".join(selected if selected is not None else fields))
    if via: out += " via " + via
    if filters: out += " where " + (filters[0] if len(filters) == 1 else "(" + " AND ".join(filters) + ")")
    if order: out += " order " + ", ".join(order)
    if limit is not None: out += " limit %d" % limit
    if offset is not None: out += " offset %d" % offset
    if children: out += " [" + "; ".join(children) + "]"
    return out

doc = {"spec": "semantic-query-v1", "model": MODEL, "cases": [dict(c, **expected(c)) for c in CASES]}
path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "semantic-query-vectors.json")
with open(path, "w", newline="\n") as f:
    json.dump(doc, f, indent=2); f.write("\n")
print("wrote", os.path.normpath(path))
