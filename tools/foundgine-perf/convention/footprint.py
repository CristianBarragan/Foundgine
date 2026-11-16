#!/usr/bin/env python3
"""Scaffolding footprint: how much code a human writes vs what Foundgine generates/derives.

usage: footprint.py --src DIR [--generated DIR] [--json out.json]

Counts (non-blank, non-comment C# lines):
  handwritten_loc      all .cs under --src (excluding obj/bin and *.g.cs)
  generated_loc        *.g.cs under --generated (build with EmitCompilerGeneratedFiles=true,
                       CompilerGeneratedFilesOutputPath=<dir>); omitted if not supplied
  entity/field/relationship attributes, and fields that rely on CONVENTION (no explicit
  StorageName) vs fields that override it.
  manual_registrations  hand-built SemanticModelBuilder/.Entity(/MetadataRegistry.Register( calls,
                        i.e. places where the convention/generator path is NOT used.
"""
import argparse, json, os, re

def loc(path):
    n, in_block = 0, False
    for line in open(path, encoding="utf-8-sig", errors="ignore"):
        s = line.strip()
        if in_block:
            if "*/" in s: in_block = False
            continue
        if not s or s.startswith("//"): continue
        if s.startswith("/*"):
            in_block = "*/" not in s; continue
        n += 1
    return n

def walk(root, pat):
    for d, dirs, files in os.walk(root):
        dirs[:] = [x for x in dirs if x not in ("obj", "bin", "node_modules", ".git")]
        for f in files:
            if re.search(pat, f): yield os.path.join(d, f)

def analyse(src, generated=None):
    hand = [p for p in walk(src, r"\.cs$") if not p.endswith(".g.cs")]
    text = {p: open(p, encoding="utf-8-sig", errors="ignore").read() for p in hand}
    all_txt = "\n".join(text.values())
    fields = re.findall(r"\[FoundgineField\(([^\]]*)\)\]", all_txt)
    out = {"files": len(hand), "handwritten_loc": sum(loc(p) for p in hand),
           "entity_attributes": len(re.findall(r"\[FoundgineEntity\b", all_txt)),
           "field_attributes": len(fields),
           "relationship_attributes": len(re.findall(r"\[FoundgineRelationship\b", all_txt)),
           "fields_overriding_storage_name": sum("StorageName" in f for f in fields),
           "manual_registrations": len(re.findall(r"new SemanticModelBuilder\(|\.Entity\(|MetadataRegistry\(\)|registry\.Register\(", all_txt))}
    out["fields_by_convention"] = out["field_attributes"] - out["fields_overriding_storage_name"]
    if generated:
        g = list(walk(generated, r"\.g\.cs$"))
        out["generated_files"], out["generated_loc"] = len(g), sum(loc(p) for p in g)
        if out["handwritten_loc"]:
            out["generated_per_handwritten"] = round(out["generated_loc"] / out["handwritten_loc"], 2)
    return out

if __name__ == "__main__":
    a = argparse.ArgumentParser(); a.add_argument("--src", required=True); a.add_argument("--generated")
    a.add_argument("--json"); a = a.parse_args()
    r = analyse(a.src, a.generated)
    print(json.dumps(r, indent=2))
    if a.json: json.dump(r, open(a.json, "w"), indent=2)
