package com.foundgine.providers.tools.mcp;

/** Canonical MCP tool names, identical to the C# adapter ({@code McpServerTool(Name = ...)}). */
public final class FoundgineMcpToolNames {
    private FoundgineMcpToolNames() {}

    public static final String CAPABILITIES = "foundgine_capabilities";
    public static final String QUERY = "foundgine_query";
    public static final String QUERY_SEMANTIC = "foundgine_query_semantic";
    public static final String MUTATION_DRY_RUN = "foundgine_mutation_dry_run";
    public static final String MUTATION_APPROVE = "foundgine_mutation_approve";
    public static final String MUTATION_EXECUTE_APPROVED = "foundgine_mutation_execute_approved";
}
