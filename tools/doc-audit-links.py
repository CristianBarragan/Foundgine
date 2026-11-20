#!/usr/bin/env python3
"""Check local Markdown and HTML links in repository documentation (stdlib only)."""
from __future__ import annotations

from html.parser import HTMLParser
from pathlib import Path
import re
import sys
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]

class Links(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.targets: list[str] = []
    def handle_starttag(self, tag: str, attrs) -> None:
        values = dict(attrs)
        key = "href" if tag in {"a", "link"} else "src" if tag in {"img", "script", "source"} else None
        if key and values.get(key):
            self.targets.append(values[key])

def local_target(source: Path, raw: str) -> Path | None:
    raw = unquote(raw.strip().split("#", 1)[0].split("?", 1)[0])
    if not raw or raw.startswith(("http://", "https://", "mailto:", "tel:", "data:", "javascript:")):
        return None
    if raw.startswith("/"):
        # Site-root URLs in docs-site are relative to docs-site, not repository root.
        base = ROOT / "docs-site" if source.is_relative_to(ROOT / "docs-site") else ROOT
        target = (base / raw.lstrip("/")).resolve()
    else:
        target = (source.parent / raw).resolve()
    return target

def exists_with_mkdocs_extensions(target: Path) -> bool:
    candidates = [target, Path(str(target) + ".md"), Path(str(target) + ".html"), target / "index.md", target / "index.html"]
    if any(p.exists() for p in candidates):
        return True
    # Case-insensitive fallback for case-sensitive CI/Linux file systems.
    for candidate in candidates:
        parent = candidate.parent
        if parent.exists() and candidate.name.lower() in {x.name.lower() for x in parent.iterdir()}:
            return True
    return False

def main() -> int:
    errors: list[tuple[str, str]] = []
    markdown = list(ROOT.rglob("*.md")) + list(ROOT.rglob("*.txt"))
    for source in markdown:
        text = source.read_text(errors="replace")
        for match in re.finditer(r"(?<!!)\[[^\]]*\]\(([^)]+)\)", text):
            target = local_target(source, match.group(1))
            if target is not None and not exists_with_mkdocs_extensions(target):
                errors.append((source.relative_to(ROOT).as_posix(), match.group(1)))
    for source in ROOT.rglob("*.html"):
        parser = Links()
        parser.feed(source.read_text(errors="replace"))
        for raw in parser.targets:
            target = local_target(source, raw)
            if target is not None and not exists_with_mkdocs_extensions(target):
                errors.append((source.relative_to(ROOT).as_posix(), raw))
    print(f"Checked {len(markdown)} Markdown/text files and HTML local references.")
    if errors:
        print(f"Missing local links: {len(errors)}")
        for source, target in errors:
            print(f"{source}: {target}")
        return 1
    print("No missing local links detected.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
