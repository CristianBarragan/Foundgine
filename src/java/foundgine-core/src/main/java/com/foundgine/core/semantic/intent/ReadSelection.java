package com.foundgine.core.semantic.intent;
import java.util.*;
public record ReadSelection(String field, String relationship, List<ReadSelection> children) {
    public ReadSelection { if (field != null && relationship != null) throw new IllegalArgumentException("A selection must specify exactly one field or relationship."); children=children==null?List.of():List.copyOf(children); }
    public ReadSelection(String field) { this(field,null,List.of()); }
    public ReadSelection(String field,String relationship) { this(field,relationship,List.of()); }
    public List<ReadSelection> effectiveChildren(){return children;}
}
