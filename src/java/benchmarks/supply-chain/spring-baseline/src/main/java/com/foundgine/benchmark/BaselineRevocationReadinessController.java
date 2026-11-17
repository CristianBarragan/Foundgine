package com.foundgine.benchmark;
import org.springframework.http.ResponseEntity; import org.springframework.web.bind.annotation.GetMapping; import org.springframework.web.bind.annotation.RestController;
@RestController public class BaselineRevocationReadinessController {
 private final BaselineRevocationEpochGuard guard; public BaselineRevocationReadinessController(BaselineRevocationEpochGuard g){guard=g;}
 @GetMapping("/health/ready") public ResponseEntity<?> ready(){try{return ResponseEntity.ok(java.util.Map.of("status","ready","revocationSequence",guard.probe()));}catch(RuntimeException e){return ResponseEntity.status(503).body(java.util.Map.of("status","not-ready","reason","revocation-state-unavailable"));}}
}
