package com.foundgine.core.semantic.configuration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import java.util.List;
import org.junit.jupiter.api.Test;

/** Exercises the fluent configuration API with the example from the design proposal. */
class FoundgineModelConfigurationTest {

    // ---- application models: no Foundgine dependency ------------------------------------

    public static final class Customer {
        public int getId() { return 0; }
        public String getName() { return ""; }
        public String getEmail() { return ""; }
        public List<Order> getOrders() { return List.of(); }
    }

    public static final class Order {
        public int getId() { return 0; }
        public int getCustomerId() { return 0; }
        public Customer getCustomer() { return null; }
    }

    // ---- storage models -------------------------------------------------------------------

    public static final class ErpCustomer {
        public int getId() { return 0; }
        public String getName() { return ""; }
        public String getEmail() { return ""; }
    }

    public static final class ErpOrder {
        public int getId() { return 0; }
        public int getCustomerId() { return 0; }
    }

    // ---- configurations -------------------------------------------------------------------

    static final class CustomerModelConfiguration extends FoundgineModelConfiguration<Customer> {
        @Override
        public void configure(FoundgineModelBuilder<Customer> model) {
            model.toEntity("Customer");
            model.hasAlias("Client");
            model.hasAlias("Account", 90);
            model.hasDimension(Customer::getName).as("CustomerName");
            model.hasDimension(Customer::getEmail).as("EmailAddress");
        }
    }

    static final class OrderModelConfiguration extends FoundgineModelConfiguration<Order> {
        @Override
        public void configure(FoundgineModelBuilder<Order> model) {
            model.toEntity("Order");
            model.hasRelationship(Order::getCustomer).to(Customer.class).withMany(Customer::getOrders);
        }
    }

    static final class ErpCustomerConfiguration extends FoundginePersistenceConfiguration<ErpCustomer> {
        @Override
        public void configure(FoundginePersistenceBuilder<ErpCustomer> model) {
            model.toTable("customers", "erp");
            model.hasKey(ErpCustomer::getId);
            model.property(ErpCustomer::getName)
                    .hasColumnName("customer_name")
                    .hasMaxLength(200)
                    .isRequired();
            model.hasIndex(ErpCustomer::getEmail);
        }
    }

    static final class ErpOrderConfiguration extends FoundginePersistenceConfiguration<ErpOrder> {
        @Override
        public void configure(FoundginePersistenceBuilder<ErpOrder> model) {
            model.toTable("orders", "sales");
            model.hasKey(ErpOrder::getId);
            model.property(ErpOrder::getCustomerId).hasColumnName("customer_id");
        }
    }

    static final class CustomerEntityMapping extends FoundgineEntityMap<Customer, ErpCustomer> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Customer, ErpCustomer> map) {
            map.property(Customer::getId).to(ErpCustomer::getId);
            map.property(Customer::getName).to(ErpCustomer::getName);
            map.property(Customer::getEmail).to(ErpCustomer::getEmail);
        }
    }

    static final class OrderEntityMapping extends FoundgineEntityMap<Order, ErpOrder> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Order, ErpOrder> map) {
            map.property(Order::getId).to(ErpOrder::getId);
            map.property(Order::getCustomerId).to(ErpOrder::getCustomerId);
        }
    }

    static final class CustomerOrderConnection extends FoundgineConnectionConfiguration {
        @Override
        public void configure(FoundgineConnectionBuilder connections) {
            connections.hasRelationship(Customer.class, Order.class)
                    .using(ErpCustomer.class, ErpOrder.class)
                    .hasForeignKey(ErpOrder::getCustomerId)
                    .references(ErpCustomer::getId);
        }
    }

    static final class ErpModel extends FoundgineModel {
        @Override
        public void configure(FoundgineModelRegistrar model) {
            model.configure(CustomerModelConfiguration.class);
            model.configure(OrderModelConfiguration.class);
            model.configure(ErpCustomerConfiguration.class);
            model.configure(ErpOrderConfiguration.class);
            model.configure(CustomerEntityMapping.class);
            model.configure(OrderEntityMapping.class);
            model.configure(CustomerOrderConnection.class);
        }
    }

    // ---- tests ----------------------------------------------------------------------------

    private static EntityMetadata entity(MetadataRegistry registry, String name) {
        return registry.get(EntityId.create(name));
    }

    @Test
    void buildsEntityMetadataFromTheProposalExample() {
        MetadataRegistry registry = new ErpModel().build();

        EntityMetadata customer = entity(registry, "Customer");
        assertEquals("erp.customers", customer.storageName());
        assertEquals(Customer.class, customer.clrType());
        assertEquals(List.of("Client", "Account"), customer.effectiveAliases().stream().map(Object::toString).toList());
        assertEquals(Integer.valueOf(90), customer.effectiveAliases().get(1).weight());

        ColumnId nameColumn = ColumnId.create("erp.customers", "customer_name");
        ColumnMetadata column =
                customer.columns().stream().filter(c -> c.id().equals(nameColumn)).findFirst().orElseThrow();
        assertEquals("customer_name", column.name());
        assertEquals(Integer.valueOf(200), column.maxLength());
        assertTrue(column.required());

        FieldMetadata name =
                customer.effectiveFields().stream().filter(f -> f.name().equals("name")).findFirst().orElseThrow();
        assertEquals(FieldId.create("Customer", "name"), name.id());
        assertEquals("CustomerName", name.dimension());
        assertEquals(String.class, name.clrType());
        assertFalse(name.indexed());

        FieldMetadata email =
                customer.effectiveFields().stream().filter(f -> f.name().equals("email")).findFirst().orElseThrow();
        assertTrue(email.indexed());
        assertEquals("EmailAddress", email.dimension());

        assertEquals(ColumnId.create("erp.customers", "id"), customer.primaryKey().columnId());
    }

    @Test
    void connectionConfiguresBothDirectionsOfARelationship() {
        MetadataRegistry registry = new ErpModel().build();
        EntityMetadata customer = entity(registry, "Customer");
        EntityMetadata order = entity(registry, "Order");

        ColumnId customerId = ColumnId.create("erp.customers", "id");
        ColumnId foreignKey = ColumnId.create("sales.orders", "customer_id");

        // Declared on Order; the inverse Customer.orders comes from withMany(...).
        RelationshipMetadata toCustomer = registry.get(RelationshipId.create("Order", "customer"));
        assertEquals(order.entityId(), toCustomer.source());
        assertEquals(customer.entityId(), toCustomer.target());
        assertFalse(toCustomer.collection());
        assertEquals(foreignKey, toCustomer.sourceKey().columnId());
        assertEquals(customerId, toCustomer.targetKey().columnId());

        RelationshipMetadata toOrders = registry.get(RelationshipId.create("Customer", "orders"));
        assertEquals(customer.entityId(), toOrders.source());
        assertEquals(order.entityId(), toOrders.target());
        assertTrue(toOrders.collection());
        assertEquals(customerId, toOrders.sourceKey().columnId());
        assertEquals(foreignKey, toOrders.targetKey().columnId());
    }

    @Test
    void missingConnectionAndMissingKeyAreReportedTogether() {
        FoundgineModel incomplete =
                new FoundgineModel() {
                    @Override
                    public void configure(FoundgineModelRegistrar model) {
                        model.configure(CustomerModelConfiguration.class);
                        model.configure(OrderModelConfiguration.class);
                        model.configure(
                                new FoundginePersistenceConfiguration<ErpCustomer>(ErpCustomer.class) {
                                    @Override
                                    public void configure(FoundginePersistenceBuilder<ErpCustomer> m) {
                                        m.toTable("customers");
                                    }
                                });
                        model.configure(ErpOrderConfiguration.class);
                        model.configure(CustomerEntityMapping.class);
                        model.configure(OrderEntityMapping.class);
                    }
                };

        IllegalStateException failure = assertThrows(IllegalStateException.class, incomplete::build);
        assertTrue(failure.getMessage().contains("ErpCustomer: no key declared"), failure.getMessage());
        assertTrue(failure.getMessage().contains("no connection configured"), failure.getMessage());
    }

    @Test
    void lambdaSelectorsAreRejectedWithAClearMessage() {
        FoundgineModel model =
                new FoundgineModel() {
                    @Override
                    public void configure(FoundgineModelRegistrar registrar) {
                        registrar.configure(
                                new FoundgineModelConfiguration<Customer>(Customer.class) {
                                    @Override
                                    public void configure(FoundgineModelBuilder<Customer> m) {
                                        m.hasDimension(c -> c.getName().trim());
                                    }
                                });
                    }
                };

        IllegalArgumentException failure = assertThrows(IllegalArgumentException.class, model::build);
        assertTrue(failure.getMessage().contains("method references"), failure.getMessage());
    }

    @Test
    void buildIntoRefusesToOverwriteExistingMetadata() {
        MetadataRegistry registry = new MetadataRegistry();
        new ErpModel().buildInto(registry);

        IllegalStateException failure =
                assertThrows(IllegalStateException.class, () -> new ErpModel().buildInto(registry));
        assertTrue(failure.getMessage().contains("entity 'Customer'"), failure.getMessage());
    }

    @Test
    void propertyNamesFollowJavaBeanAndRecordConventions() {
        assertEquals("name", PropertyReflection.propertyName("getName"));
        assertEquals("active", PropertyReflection.propertyName("isActive"));
        assertEquals("URL", PropertyReflection.propertyName("getURL"));
        assertEquals("tenantId", PropertyReflection.propertyName("tenantId"));
    }

    // ---- relationships declared by name (records have no navigation members) -------------

    public record Team(int id, String name) {}

    public record Member(int id, int teamId, int mentorId) {}

    static final class TeamModel extends FoundgineModelConfiguration<Team> {
        @Override
        public void configure(FoundgineModelBuilder<Team> model) {
            model.toEntity("Team");
            model.hasMany("members", Member.class);
        }
    }

    static final class MemberModel extends FoundgineModelConfiguration<Member> {
        @Override
        public void configure(FoundgineModelBuilder<Member> model) {
            model.toEntity("Member");
            model.hasOne("team", Team.class).withMany("members");
            model.hasOne("mentor", Member.class).withMany("mentees");
        }
    }

    static final class TeamStorage extends FoundginePersistenceConfiguration<Team> {
        @Override
        public void configure(FoundginePersistenceBuilder<Team> model) {
            model.toTable("Team");
            model.hasKey(Team::id);
        }
    }

    static final class MemberStorage extends FoundginePersistenceConfiguration<Member> {
        @Override
        public void configure(FoundginePersistenceBuilder<Member> model) {
            model.toTable("Member");
            model.hasKey(Member::id);
        }
    }

    static final class TeamMap extends FoundgineEntityMap<Team, Team> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Team, Team> map) {
            map.property(Team::id).to(Team::id);
            map.property(Team::name).to(Team::name);
        }
    }

    static final class MemberMap extends FoundgineEntityMap<Member, Member> {
        @Override
        public void configure(FoundgineEntityMapBuilder<Member, Member> map) {
            map.property(Member::id).to(Member::id);
            map.property(Member::teamId).to(Member::teamId);
            map.property(Member::mentorId).to(Member::mentorId);
        }
    }

    static final class TeamMembership extends FoundgineConnectionConfiguration {
        @Override
        public void configure(FoundgineConnectionBuilder connections) {
            connections.hasRelationship(Team.class, Member.class)
                    .using(Team.class, Member.class)
                    .hasForeignKey(Member::teamId)
                    .references(Team::id);
            // Member relates to itself, so the named navigation says which relationship this is.
            connections.hasRelationship(Member.class, Member.class, "mentor")
                    .using(Member.class, Member.class)
                    .hasForeignKeyOnSource(Member::mentorId)
                    .references(Member::id);
        }
    }

    @Test
    void relationshipsCanBeDeclaredByNameForModelsWithoutNavigationMembers() {
        MetadataRegistry registry =
                new FoundgineModel() {
                    @Override
                    public void configure(FoundgineModelRegistrar model) {
                        model.configure(TeamModel.class);
                        model.configure(MemberModel.class);
                        model.configure(TeamStorage.class);
                        model.configure(MemberStorage.class);
                        model.configure(TeamMap.class);
                        model.configure(MemberMap.class);
                        model.configure(TeamMembership.class);
                    }
                }.build();

        ColumnId teamId = ColumnId.create("Team", "id");
        ColumnId memberTeamId = ColumnId.create("Member", "teamId");

        RelationshipMetadata members = registry.get(RelationshipId.create("Team", "members"));
        assertTrue(members.collection());
        assertEquals(teamId, members.sourceKey().columnId());
        assertEquals(memberTeamId, members.targetKey().columnId());

        RelationshipMetadata team = registry.get(RelationshipId.create("Member", "team"));
        assertFalse(team.collection());
        assertEquals(memberTeamId, team.sourceKey().columnId());
        assertEquals(teamId, team.targetKey().columnId());

        // Self-relationship: the named navigation picks the connection; withMany names the inverse.
        RelationshipMetadata mentor = registry.get(RelationshipId.create("Member", "mentor"));
        assertFalse(mentor.collection());
        assertEquals(ColumnId.create("Member", "mentorId"), mentor.sourceKey().columnId());
        assertEquals(ColumnId.create("Member", "id"), mentor.targetKey().columnId());
        RelationshipMetadata mentees = registry.get(RelationshipId.create("Member", "mentees"));
        assertTrue(mentees.collection());
        assertEquals(ColumnId.create("Member", "id"), mentees.sourceKey().columnId());
        assertEquals(ColumnId.create("Member", "mentorId"), mentees.targetKey().columnId());
    }

    @Test
    void relationshipsDeclaredByNameRejectDuplicatesAndBlankNames() {
        FoundgineModel duplicate =
                new FoundgineModel() {
                    @Override
                    public void configure(FoundgineModelRegistrar registrar) {
                        registrar.configure(
                                new FoundgineModelConfiguration<Team>(Team.class) {
                                    @Override
                                    public void configure(FoundgineModelBuilder<Team> m) {
                                        m.hasMany("members", Member.class);
                                        m.hasMany("members", Member.class);
                                    }
                                });
                    }
                };
        IllegalStateException failure = assertThrows(IllegalStateException.class, duplicate::build);
        assertTrue(failure.getMessage().contains("declared more than once"), failure.getMessage());

        FoundgineModel blank =
                new FoundgineModel() {
                    @Override
                    public void configure(FoundgineModelRegistrar registrar) {
                        registrar.configure(
                                new FoundgineModelConfiguration<Team>(Team.class) {
                                    @Override
                                    public void configure(FoundgineModelBuilder<Team> m) {
                                        m.hasOne(" ", Member.class);
                                    }
                                });
                    }
                };
        assertThrows(IllegalArgumentException.class, blank::build);
    }

    static final class PascalTeamModel extends FoundgineModelConfiguration<Team> {
        @Override
        public void configure(FoundgineModelBuilder<Team> model) {
            model.toEntity("Team");
            model.hasFieldNaming(FieldNaming.PASCAL_CASE);
            model.hasDimension(Team::name).as("teamName");
            model.property(Team::name).hasAlias("Squad");
        }
    }

    @Test
    void fieldNamingControlsSemanticFieldNamesAndIds() {
        MetadataRegistry registry =
                new FoundgineModel() {
                    @Override
                    public void configure(FoundgineModelRegistrar model) {
                        model.configure(PascalTeamModel.class);
                        model.configure(TeamStorage.class);
                        model.configure(TeamMap.class);
                    }
                }.build();

        EntityMetadata team = registry.get(EntityId.create("Team"));
        FieldMetadata name =
                team.effectiveFields().stream().filter(f -> f.name().equals("Name")).findFirst().orElseThrow();
        assertEquals(FieldId.create("Team", "Name"), name.id());
        assertEquals("teamName", name.dimension());
        assertEquals("Squad", name.effectiveAliases().get(0).name());
        // Configuration still refers to members by their declared name; the column is unaffected.
        assertEquals(ColumnId.create("Team", "name"), name.column().columnId());
    }
}
