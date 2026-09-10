package com.foundgine.runtime.api;
import com.foundgine.core.semantic.SemanticRequest;
import java.time.OffsetDateTime;
public record PlanApproval(SemanticRequest request,String approvalId,String planFingerprint,String semanticModelVersion,int capabilityContractVersion,int capabilityVersion,int intentVersion,int planVersion,String approvedBy,OffsetDateTime approvedAt) {}
