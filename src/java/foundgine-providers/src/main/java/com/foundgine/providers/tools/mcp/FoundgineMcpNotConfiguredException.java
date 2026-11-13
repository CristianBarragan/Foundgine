package com.foundgine.providers.tools.mcp;

/** A tool was called but the host did not configure the capability behind it. The message is safe to show to an agent. */
public final class FoundgineMcpNotConfiguredException extends IllegalStateException {
    public FoundgineMcpNotConfiguredException(String message) { super(message); }
}
