package com.foundgine.providers.models;
import com.foundgine.core.execution.*; import com.foundgine.core.semantic.security.execution.*; import java.util.*; import java.util.concurrent.*;
/** Framework-neutral AI tool projection over the Foundgine runtime. */
public final class FoundgineAiToolset {public record Tool(String name,String description){} private final List<Tool> tools; public FoundgineAiToolset(Collection<Tool> tools){this.tools=List.copyOf(tools);} public List<Tool> tools(){return tools;}}
