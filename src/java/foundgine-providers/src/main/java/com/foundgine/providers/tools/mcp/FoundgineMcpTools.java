package com.foundgine.providers.tools.mcp;
import com.fasterxml.jackson.databind.*; import com.foundgine.runtime.*; import java.util.*; import java.util.concurrent.*;
/** MCP-neutral tool contract. A transport adapter maps tools/call onto invoke. */
public final class FoundgineMcpTools {public interface Executor {CompletionStage<Object> execute(String intentJson);} private final Executor executor;private final ObjectMapper mapper=new ObjectMapper();public FoundgineMcpTools(Executor e){executor=Objects.requireNonNull(e);}public CompletionStage<String> foundgineQuery(String intentJson){return executor.execute(intentJson).thenApply(x->{try{return mapper.writeValueAsString(x);}catch(Exception e){throw new CompletionException(e);}});}}
