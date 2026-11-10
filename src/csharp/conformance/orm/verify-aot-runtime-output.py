#!/usr/bin/env python3
"""Require an exact semantic success marker in native AOT runtime output."""
import argparse
from pathlib import Path


def verify(log_path: Path, expected_marker: str) -> None:
    if not expected_marker.strip():
        raise ValueError('expected success marker must not be empty')
    if not log_path.is_file():
        raise FileNotFoundError(f'native runtime log does not exist: {log_path}')
    lines = log_path.read_text(encoding='utf-8', errors='replace').splitlines()
    if expected_marker not in lines:
        raise ValueError(f'expected native runtime success marker was not emitted: {expected_marker}')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('log', type=Path)
    parser.add_argument('--expected-marker', required=True)
    args = parser.parse_args()
    try:
        verify(args.log, args.expected_marker)
    except (OSError, ValueError) as exc:
        parser.error(str(exc))
    print('Native AOT semantic success marker verified.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
