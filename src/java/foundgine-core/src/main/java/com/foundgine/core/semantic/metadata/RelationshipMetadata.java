package com.foundgine.core.semantic.metadata;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.RelationshipId;
import java.util.List;
public record RelationshipMetadata(RelationshipId id, EntityId source, EntityId target, String name,
                                   ColumnReference sourceKey, ColumnReference targetKey,
                                   boolean collection, List<AliasDeclaration> aliases) {
    public RelationshipMetadata(RelationshipId id, EntityId source, EntityId target, String name,
                                ColumnReference sourceKey, ColumnReference targetKey) {
        this(id,source,target,name,sourceKey,targetKey,true,null);
    }
}
