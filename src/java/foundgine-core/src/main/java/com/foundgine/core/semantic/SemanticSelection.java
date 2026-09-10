package com.foundgine.core.semantic;
import com.foundgine.core.abstractions.*; import java.util.*;
public record SemanticSelection(FieldId field,RelationshipId relationship,List<SemanticSelection> children){public SemanticSelection{children=List.copyOf(children==null?List.of():children);if(field!=null&&relationship!=null)throw new IllegalArgumentException("A semantic selection cannot contain both a field and relationship.");}}
