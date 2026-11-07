package com.foundgine.core.semantic.configuration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.AuthorizationPredicateKind;
import com.foundgine.core.abstractions.ConnectionId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.semantic.metadata.AuthorizationMetadata;
import com.foundgine.core.semantic.metadata.ConnectionFieldMetadata;
import com.foundgine.core.semantic.metadata.ConnectionMetadata;
import com.foundgine.core.semantic.metadata.ConversionMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

/** Model-to-entity connections, delegate conversions and authorization predicates. */
class FoundgineModelConnectionConfigurationTest {

    public static final class Contract {
        public int getId() { return 0; }
        public String getCode() { return ""; }
    }

    public static final class ContractRow {
        public int getId() { return 0; }
        public int getCodeNumber() { return 0; }
        public String getTenantId() { return ""; }
        public boolean isActive() { return false; }
    }

    public static final class Party {
        public int getId() { return 0; }
        public Object getContract() { return null; }
    }

    public static final class PartyRow {
        public int getId() { return 0; }
    }

    public static final class UserContext {
        public String getTenantId() { return ""; }
    }

    public static final class Unmapped {
        public int getId() { return 0; }
    }

    static final class ContractModel extends FoundgineModelConfiguration<Contract> {
        @Override
        public void configure(FoundgineModelBuilder<Contract> model) {
            model.toEntity("Contract");
        }
    }

    static final class ContractPersistence extends FoundginePersistenceConfiguration<ContractRow> {
        @Override
        public void configure(FoundginePersistenceBuilder<ContractRow> model) {
            model.toTable("contracts");
            model.hasKey(ContractRow::getId);
        }
    }

    static final class ContractMap extends FoundgineEntityMap<Contract, ContractRow> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Contract, ContractRow> map) {
            map.property(Contract::getId).to(ContractRow::getId);
        }
    }

    static final class PartyModel extends FoundgineModelConfiguration<Party> {
        @Override
        public void configure(FoundgineModelBuilder<Party> model) {
            model.toEntity("Party");
        }
    }

    static final class PartyPersistence extends FoundginePersistenceConfiguration<PartyRow> {
        @Override
        public void configure(FoundginePersistenceBuilder<PartyRow> model) {
            model.toTable("parties");
            model.hasKey(PartyRow::getId);
        }
    }

    static final class PartyMap extends FoundgineEntityMap<Party, PartyRow> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Party, PartyRow> map) {
            map.property(Party::getId).to(PartyRow::getId);
        }
    }

    static final class PartyConnections extends FoundgineModelConnectionConfiguration<Party> {
        @Override
        public void configure(FoundgineModelConnectionBuilder<Party> connections) {
            connections
                    .hasConnection(Party::getContract, ContractRow.class)
                    .maps(Party::getId, ContractRow::getId)
                    .authorize(
                            UserContext.class,
                            "CanVisit",
                            a ->
                                    a.context(UserContext::getTenantId)
                                            .eq(a.resource(ContractRow::getTenantId))
                                            .and(a.resource(ContractRow::isActive).not()));
        }
    }

    /** Declared as a field so the same function instance can be reused across fields. */
    static final java.util.function.Function<String, Integer> PARSE = Integer::parseInt;

    static final class ConvertingConnections extends FoundgineModelConnectionConfiguration<Contract> {
        @Override
        public void configure(FoundgineModelConnectionBuilder<Contract> connections) {
            connections
                    .hasConnection(Contract::getCode, ContractRow.class)
                    .hasName("ByCode")
                    .maps(Contract::getCode, ContractRow::getCodeNumber, PARSE);
        }
    }

    static final class UnmappedTarget extends FoundgineModelConnectionConfiguration<Contract> {
        @Override
        public void configure(FoundgineModelConnectionBuilder<Contract> connections) {
            connections.hasConnection("Other", Unmapped.class);
        }
    }

    static final class MismatchedTypes extends FoundgineModelConnectionConfiguration<Contract> {
        @Override
        @SuppressWarnings({"unchecked", "rawtypes"})
        public void configure(FoundgineModelConnectionBuilder<Contract> connections) {
            // Raw types bypass the compile-time check to prove the build reports it as well.
            ((FoundgineModelConnectionEntityBuilder) connections.hasConnection(Contract::getCode, ContractRow.class))
                    .maps((PropertyRef) (PropertyRef<Contract, String>) Contract::getCode,
                            (PropertyRef) (PropertyRef<ContractRow, Integer>) ContractRow::getCodeNumber);
        }
    }

    static final class Model extends FoundgineModel {
        private final List<IFoundgineConfiguration> extra = new ArrayList<>();

        Model(IFoundgineConfiguration... extra) {
            this.extra.addAll(List.of(extra));
        }

        @Override
        public void configure(FoundgineModelRegistrar model) {
            model.configure(new ContractModel());
            model.configure(new ContractPersistence());
            model.configure(new ContractMap());
            model.configure(new PartyModel());
            model.configure(new PartyPersistence());
            model.configure(new PartyMap());
            extra.forEach(model::configure);
        }
    }

    @Test
    void connectionRegistersTargetFieldsAndId() {
        MetadataRegistry registry = new Model(new ConvertingConnections()).build();

        ConnectionMetadata connection = single(registry.connections());
        assertEquals(ConnectionId.create("Contract", "ByCode"), connection.id());
        assertEquals("ByCode", connection.name());
        assertEquals("code", connection.sourceMember());
        assertEquals(EntityId.create("Contract"), connection.target());

        assertEquals(1, connection.fields().size());
        ConnectionFieldMetadata field = connection.fields().get(0);
        assertEquals("code", field.sourceMember());
        assertEquals("codeNumber", field.targetMember());
        assertEquals(String.class, field.sourceType());
        assertEquals(int.class, field.targetType());
        assertEquals("fluent:String->Integer", field.converter());
    }

    @Test
    void functionConversionIsRegisteredAndCallable() {
        MetadataRegistry registry = new Model(new ConvertingConnections()).build();

        ConversionMetadata conversion = registry.findConversion(String.class, Integer.class);
        assertNotNull(conversion);
        assertEquals(42, conversion.convert().apply("42"));
    }

    @Test
    void authorizationPredicateIsLowered() {
        MetadataRegistry registry = new Model(new PartyConnections()).build();

        AuthorizationMetadata authorization = single(registry.authorizations());
        assertEquals(single(registry.connections()).id(), authorization.connectionId());
        assertEquals("CanVisit", authorization.name());
        assertEquals(UserContext.class, authorization.contextType());
        assertEquals(ContractRow.class, authorization.resourceType());
        assertEquals("((context.tenantId == resource.tenantId) && !resource.active)", authorization.expression());

        AuthorizationPredicate and = authorization.predicate();
        assertEquals(AuthorizationPredicateKind.AND, and.kind());
        AuthorizationPredicate equal = and.left();
        assertEquals(AuthorizationPredicateKind.EQUAL, equal.kind());
        assertEquals(AuthorizationPredicateKind.MEMBER_ACCESS, equal.left().kind());
        assertEquals("tenantId", equal.left().name());
        assertEquals(AuthorizationPredicateKind.CONTEXT_PARAMETER, equal.left().left().kind());
        assertEquals(AuthorizationPredicateKind.RESOURCE_PARAMETER, equal.right().left().kind());

        AuthorizationPredicate not = and.right();
        assertEquals(AuthorizationPredicateKind.NOT, not.kind());
        assertEquals("active", not.left().name());
    }

    @Test
    void constantsAreFormattedLikeTheOtherLanguages() {
        assertEquals("\"admin\"", new AuthorizationOperands<UserContext, ContractRow>().constant("admin").toString());
        assertEquals("5", new AuthorizationOperands<UserContext, ContractRow>().constant(5).toString());
        assertEquals("true", new AuthorizationOperands<UserContext, ContractRow>().constant(true).toString());
        assertThrows(IllegalArgumentException.class,
                () -> new AuthorizationOperands<UserContext, ContractRow>().constant(new Object()));
    }

    @Test
    void mismatchedTypesWithoutConverterAreReported() {
        IllegalStateException error =
                assertThrows(IllegalStateException.class, () -> new Model(new MismatchedTypes()).build());
        assertTrue(error.getMessage().contains("differ in type"), error.getMessage());
    }

    @Test
    void connectionToUnmappedEntityIsReported() {
        IllegalStateException error =
                assertThrows(IllegalStateException.class, () -> new Model(new UnmappedTarget()).build());
        assertTrue(error.getMessage().contains("not the storage type of any entity map"), error.getMessage());
    }

    @Test
    void buildIntoRejectsDuplicateConnection() {
        MetadataRegistry registry = new Model(new ConvertingConnections()).build();
        IllegalStateException error =
                assertThrows(
                        IllegalStateException.class,
                        () -> new Model(new ConvertingConnections()).buildInto(registry));
        assertTrue(error.getMessage().contains("connection 'ByCode'"), error.getMessage());
    }

    @Test
    void connectionlessModelsStillBuild() {
        MetadataRegistry registry = new Model().build();
        assertFalse(registry.connections().iterator().hasNext());
        assertFalse(registry.authorizations().iterator().hasNext());
        assertFalse(registry.conversions().iterator().hasNext());
    }

    private static <T> T single(Iterable<T> items) {
        List<T> list = new ArrayList<>();
        items.forEach(list::add);
        assertEquals(1, list.size());
        return list.get(0);
    }
}
