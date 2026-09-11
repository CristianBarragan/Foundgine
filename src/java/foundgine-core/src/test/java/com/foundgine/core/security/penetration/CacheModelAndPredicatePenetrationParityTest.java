package com.foundgine.core.security.penetration;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.planning.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

/** Port of cache/model/predicate isolation penetration tests. */
class CacheModelAndPredicatePenetrationParityTest {
    @Test void cacheKeyIsolationDoesNotAliasDelimiterShapedAuthorities(){
        var cache=new MemoryProviderPlanCache();
        var a=new ProviderPlan("a"){}; var b=new ProviderPlan("b"){};
        cache.set("tenant=a\u001fsubject=b",a); cache.set("tenant=a\u001fsubject=b\u001fc",b);
        assertEquals(a,cache.tryGet("tenant=a\u001fsubject=b")); assertEquals(b,cache.tryGet("tenant=a\u001fsubject=b\u001fc"));
    }
    @Test void securityObligationChangesProduceDifferentPlanFingerprints(){
        var a=plan(List.of("security.tenant-isolation")); var b=plan(List.of("security.authorization-required"));
        assertTrue(!SemanticPlanFingerprint.create(a).equals(SemanticPlanFingerprint.create(b)));
    }
    @Test void unknownSecurityObligationIsNotNormalizedIntoKnownOne(){
        var p=plan(List.of("security.unknown"));
        assertTrue(p.requiredSecurityInvariants().contains("security.unknown"));
        assertTrue(!p.requiredSecurityInvariants().contains("security.authorization-required"));
    }
    private static SemanticPlan plan(List<String> invariants){
        var n=new SemanticPlanNode(1,ExecutionOperation.SCAN,new EntityId(1),List.of(new FieldId(1)),null,null,List.of());
        return new SemanticPlan(n,invariants,null);
    }
}
