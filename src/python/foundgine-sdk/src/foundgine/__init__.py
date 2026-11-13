"""Python SDK for Foundgine (transport only). Mirrors @foundgine/sdk."""

from .client import DEFAULT_TIMEOUT, FoundgineClient, Tool, ToolResult
from .errors import (
    FoundgineAbortError,
    FoundgineError,
    FoundgineHttpError,
    FoundgineResyncRequiredError,
    FoundgineRpcError,
    FoundgineSessionExpiredError,
    FoundgineWatchTimeoutError,
)
from .http import TokenProvider
from .mcp import DEFAULT_PROTOCOL_VERSION, McpSession
from .sse import SseEvent, SseParser
from .sync import FoundgineSyncClient, SyncWorkflowClient
from .workflows import (
    StartAndWatch,
    WorkflowClient,
    WorkflowProcessDescription,
    WorkflowStartResult,
    WorkflowStatus,
    WorkflowStepDescription,
    WorkflowWatch,
)

__version__ = "3.3.4"

__all__ = [
    "DEFAULT_PROTOCOL_VERSION",
    "DEFAULT_TIMEOUT",
    "FoundgineAbortError",
    "FoundgineClient",
    "FoundgineError",
    "FoundgineHttpError",
    "FoundgineResyncRequiredError",
    "FoundgineRpcError",
    "FoundgineSessionExpiredError",
    "FoundgineSyncClient",
    "FoundgineWatchTimeoutError",
    "McpSession",
    "SseEvent",
    "SseParser",
    "StartAndWatch",
    "SyncWorkflowClient",
    "TokenProvider",
    "Tool",
    "ToolResult",
    "WorkflowClient",
    "WorkflowProcessDescription",
    "WorkflowStartResult",
    "WorkflowStatus",
    "WorkflowStepDescription",
    "WorkflowWatch",
    "__version__",
]
