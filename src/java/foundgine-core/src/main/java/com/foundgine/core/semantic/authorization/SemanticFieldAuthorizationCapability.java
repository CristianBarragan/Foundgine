package com.foundgine.core.semantic.authorization;
import com.foundgine.core.abstractions.*;
public record SemanticFieldAuthorizationCapability(FieldId fieldId,String name,AuthorizationDecision read,AuthorizationDecision write){}
