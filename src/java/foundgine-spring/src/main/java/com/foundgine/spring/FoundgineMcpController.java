package com.foundgine.spring;

import com.foundgine.providers.tools.mcp.FoundgineMcpServer;
import java.util.List;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * Streamable-HTTP style MCP endpoint (JSON responses only: no SSE, sessions or resumability). Browsers' cross-origin
 * requests are refused unless their {@code Origin} is listed in {@code foundgine.mcp.allowed-origins}, which is the
 * MCP-recommended DNS-rebinding defence; requests without an Origin (non-browser agents) are not affected. Authentication
 * is the application's job and must run before this controller. The tools are invoked on the request thread, so
 * thread-bound identity (Spring Security's context holder) is visible to the host's security provider.
 */
@RestController
@RequestMapping("${foundgine.mcp.path:/mcp}")
public class FoundgineMcpController {
    private final FoundgineMcpServer server;
    private final List<String> allowedOrigins;

    public FoundgineMcpController(FoundgineMcpServer server, List<String> allowedOrigins) {
        this.server = server;
        this.allowedOrigins = allowedOrigins == null ? List.of() : allowedOrigins.stream().map(String::trim).filter(s -> !s.isEmpty()).toList();
    }

    @PostMapping(consumes = MediaType.APPLICATION_JSON_VALUE)
    public ResponseEntity<String> post(
            @RequestBody String body, @RequestHeader(value = "Origin", required = false) String origin) {
        if (origin != null && !allowedOrigins.contains(origin)) return ResponseEntity.status(HttpStatus.FORBIDDEN).build();
        var reply = server.handleAsync(body).toCompletableFuture().join();
        if (reply.isEmpty()) return ResponseEntity.accepted().build();
        return ResponseEntity.ok().contentType(MediaType.APPLICATION_JSON).body(reply.get());
    }
}
