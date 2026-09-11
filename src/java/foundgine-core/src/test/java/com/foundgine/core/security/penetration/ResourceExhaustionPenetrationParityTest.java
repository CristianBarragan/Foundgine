package com.foundgine.core.security.penetration;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.query.*;
import com.foundgine.core.semantic.security.execution.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

/** Port of resource-exhaustion penetration tests against the protocol-neutral semantic boundary. */
class ResourceExhaustionPenetrationParityTest {
    private static SecurityResourceLimits limits(int depth,int filterNodes,int page,int cursor){
        var d=SecurityResourceLimits.defaults();
        return new SecurityResourceLimits(depth,d.maxOperationGraphNodes(),d.maxOperationGraphDepth(),d.maxOperationGraphEdges(),d.maxOperationGraphFields(),d.maxSelectionNodes(),d.maxFilterDepth(),filterNodes,d.maxOrderTerms(),d.maxOrderPathDepth(),page,d.maxOffset(),cursor,d.maxMutationOperations(),d.maxMutationFieldsPerOperation(),d.maxMutationReturnFieldsPerOperation(),d.maxMutationDependencies(),d.maxMutationEffects());
    }
    @Test void hugeSelectionDepthIsRejected(){
        var s=new SemanticSelection(new FieldId(1),null,List.of(new SemanticSelection(new FieldId(1),null,List.of(new SemanticSelection(new FieldId(1),null,List.of())))));
        assertThrows(IllegalStateException.class,()->SecurityResourceLimitValidator.validate(new SemanticRequest(new EntityId(1),List.of(s),null,null),limits(2,256,1000,4096)));
    }
    @Test void hugeFilterTreeIsRejected(){
        var f=new SemanticAndFilter(List.of(new SemanticFieldFilter(new FieldId(1),SemanticFilterOperator.EQ,1),new SemanticFieldFilter(new FieldId(1),SemanticFilterOperator.EQ,2),new SemanticFieldFilter(new FieldId(1),SemanticFilterOperator.EQ,3),new SemanticFieldFilter(new FieldId(1),SemanticFilterOperator.EQ,4)));
        var q=new SemanticQueryOptions(f,List.of(),null,null,null);
        assertThrows(IllegalStateException.class,()->SecurityResourceLimitValidator.validate(new SemanticRequest(new EntityId(1),List.of(new SemanticSelection(new FieldId(1),null,List.of())),q,null),limits(32,3,1000,4096)));
    }
    @Test void hugePageSizeIsRejected(){var q=new SemanticQueryOptions(null,List.of(),1_000_000,null,null);assertThrows(IllegalStateException.class,()->SecurityResourceLimitValidator.validate(new SemanticRequest(new EntityId(1),List.of(new SemanticSelection(new FieldId(1),null,List.of())),q,null),limits(32,256,100,4096)));}
    @Test void hugeCursorIsRejected(){var q=new SemanticQueryOptions(null,List.of(),null,null,"x".repeat(10_001));assertThrows(IllegalStateException.class,()->SecurityResourceLimitValidator.validate(new SemanticRequest(new EntityId(1),List.of(new SemanticSelection(new FieldId(1),null,List.of())),q,null),limits(32,256,1000,100)));}
}
