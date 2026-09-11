package com.foundgine.core.semantic.metadata;
import com.foundgine.core.abstractions.FieldId;
import java.util.List;
public record FieldMetadata(FieldId id, String name, Class<?> clrType, ColumnReference column,
                            String dimension, boolean indexed, List<AliasDeclaration> aliases) {
    public FieldMetadata(FieldId id, String name, Class<?> clrType) { this(id,name,clrType,null,null,false,null); }
    public FieldMetadata(FieldId id, String name, Class<?> clrType, ColumnReference column) { this(id,name,clrType,column,null,false,null); }
}
