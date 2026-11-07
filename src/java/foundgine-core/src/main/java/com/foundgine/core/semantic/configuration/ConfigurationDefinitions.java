package com.foundgine.core.semantic.configuration;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.semantic.metadata.AliasDeclaration;
import com.foundgine.core.semantic.metadata.ConversionMetadata;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Mutable state collected while configurations run. Nothing here is public:
 * {@link FoundgineModelCompiler} turns it into the existing metadata records once every
 * configuration has been applied, so configuration order never matters.
 */
final class ConfigurationDefinitions {
    private ConfigurationDefinitions() {}

    static AliasDeclaration alias(String name, Integer weight) {
        if (name == null || name.isBlank()) throw new IllegalArgumentException("Alias name is required.");
        return new AliasDeclaration(name.trim(), weight);
    }

    static final class ModelDefinition {
        final Class<?> modelType;
        String entityName;
        Integer minimumWeight;
        boolean event;
        String occurredAtMember;
        FieldNaming fieldNaming = FieldNaming.AS_DECLARED;
        final List<AliasDeclaration> aliases = new ArrayList<>();
        /** Model member name to dimension name. */
        final Map<String, String> dimensions = new LinkedHashMap<>();
        final Map<String, List<AliasDeclaration>> fieldAliases = new LinkedHashMap<>();
        final List<RelationshipDefinition> relationships = new ArrayList<>();

        ModelDefinition(Class<?> modelType) {
            this.modelType = modelType;
        }
    }

    static final class RelationshipDefinition {
        final MemberRef navigation;
        final Class<?> targetModelType;
        String name;
        String inverseMember;
        String inverseName;
        boolean inverseCollection;
        final List<AliasDeclaration> aliases = new ArrayList<>();

        RelationshipDefinition(MemberRef navigation, Class<?> targetModelType) {
            this.navigation = navigation;
            this.targetModelType = targetModelType;
        }
    }

    static final class ColumnDefinition {
        final MemberRef member;
        String columnName;
        Integer maxLength;
        boolean required;

        ColumnDefinition(MemberRef member) {
            this.member = member;
        }

        String effectiveColumnName() {
            return columnName != null ? columnName : member.name();
        }
    }

    static final class PersistenceDefinition {
        final Class<?> entityType;
        String table;
        String schema;
        MemberRef key;
        final List<ColumnDefinition> columns = new ArrayList<>();
        /** Each entry is the member list of one index. */
        final List<List<MemberRef>> indexes = new ArrayList<>();

        PersistenceDefinition(Class<?> entityType) {
            this.entityType = entityType;
        }

        ColumnDefinition column(MemberRef member) {
            for (ColumnDefinition existing : columns)
                if (existing.member.name().equals(member.name())) return existing;
            ColumnDefinition created = new ColumnDefinition(member);
            columns.add(created);
            return created;
        }
    }

    record MemberPair(MemberRef model, MemberRef entity) {}

    static final class EntityMapDefinition {
        final Class<?> modelType;
        final Class<?> entityType;
        final List<MemberPair> properties = new ArrayList<>();

        EntityMapDefinition(Class<?> modelType, Class<?> entityType) {
            this.modelType = modelType;
            this.entityType = entityType;
        }
    }

    static final class ConnectionDefinition {
        final Class<?> sourceModel;
        final Class<?> targetModel;
        /** Optional member on the source model that disambiguates several relationships between the same models. */
        final String navigation;
        Class<?> sourceEntity;
        Class<?> targetEntity;
        boolean foreignKeyOnTarget = true;
        MemberRef foreignKey;
        MemberRef references;

        ConnectionDefinition(Class<?> sourceModel, Class<?> targetModel, String navigation) {
            this.sourceModel = sourceModel;
            this.targetModel = targetModel;
            this.navigation = navigation;
        }
    }

    /** A connection from a semantic model member to a storage entity ({@code ConnectionMetadata}). */
    static final class ModelConnectionDefinition {
        final Class<?> modelType;
        final Class<?> entityType;
        /** The connection member on the model, which is also the default connection name. */
        final String member;
        String name;
        Long explicitId;
        final List<ModelConnectionField> fields = new ArrayList<>();
        final List<ModelConnectionAuthorization> authorizations = new ArrayList<>();

        ModelConnectionDefinition(Class<?> modelType, Class<?> entityType, String member) {
            this.modelType = modelType;
            this.entityType = entityType;
            this.member = member;
        }

        String effectiveName() {
            return name != null ? name : member;
        }
    }

    record ModelConnectionField(
            MemberRef source, MemberRef target, ConversionMetadata conversion, Object function) {}

    record ModelConnectionAuthorization(
            String name,
            Long explicitId,
            Class<?> contextType,
            Class<?> resourceType,
            String expression,
            AuthorizationPredicate predicate) {}
}
