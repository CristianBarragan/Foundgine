"""Typed MCP capability and tool-result models."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass(frozen=True, slots=True)
class Tool:
    """A capability exposed by the MCP server."""

    name: str
    description: str | None
    input_schema: dict[str, Any]
    raw: dict[str, Any] = field(default_factory=dict, repr=False, compare=False)

    @classmethod
    def from_wire(cls, value: dict[str, Any]) -> Tool:
        """Parse an MCP tool while preserving unknown wire fields in ``raw``."""
        schema = value.get("inputSchema")
        return cls(
            str(value["name"]),
            value.get("description") if isinstance(value.get("description"), str) else None,
            schema if isinstance(schema, dict) else {},
            value,
        )


@dataclass(frozen=True, slots=True)
class ToolResult:
    """Tool output; tool-level failures are represented by ``is_error`` rather than raised."""

    content: list[dict[str, Any]]
    structured_content: Any | None
    is_error: bool
    raw: dict[str, Any] = field(default_factory=dict, repr=False, compare=False)

    @classmethod
    def from_wire(cls, value: dict[str, Any]) -> ToolResult:
        """Parse an MCP call result, retaining its original representation."""
        content = value.get("content")
        return cls(
            [item for item in content if isinstance(item, dict)] if isinstance(content, list) else [],
            value.get("structuredContent"),
            value.get("isError") is True,
            value,
        )
