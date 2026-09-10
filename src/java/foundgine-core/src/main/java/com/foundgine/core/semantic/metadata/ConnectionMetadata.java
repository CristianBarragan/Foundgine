package com.foundgine.core.semantic.metadata;
import com.foundgine.core.abstractions.ConnectionId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.ModelId;
import java.util.List;
public record ConnectionMetadata(ConnectionId id, ModelId source, EntityId target, String name,
                                 String sourceMember, List<ConnectionFieldMetadata> fields) {
    public ConnectionMetadata(ConnectionId id, ModelId source, EntityId target, String name, String sourceMember) {
        this(id,source,target,name,sourceMember,null);
    }
}
