package com.foundgine.core.semantic.planning;

import java.util.*;

public record PlanInspectionNode(int nodeId,String operation,long entityId,List<Long> fieldIds,Long viaRelationshipId,Long viaConnectionId,boolean authorizationApplied,List<PlanInspectionNode> children) {
    public PlanInspectionNode {fieldIds=fieldIds==null?List.of():List.copyOf(fieldIds);children=children==null?List.of():List.copyOf(children);}
}
