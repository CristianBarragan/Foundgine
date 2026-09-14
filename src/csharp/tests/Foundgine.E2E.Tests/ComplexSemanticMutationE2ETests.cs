using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Flagship semantic mutation scenario for PostgreSQL E2E.
///
/// This deliberately stays above provider/SQL execution. The purpose is to
/// prove that one realistic mutation graph can express the full semantic
/// vocabulary before it is lowered into execution IR and PostgreSQL.
///
/// The graph contains:
///   - Create / Update / Delete / Upsert
///   - generated-value flow across four generations
///   - direct SemanticMutationValueReference
///   - legacy explicit dependency metadata merged into the canonical edge
///   - return-field validation
///   - conflict fields
///   - Eq / Neq / In
///   - And / Or
///   - Some / None / All relationship quantifiers
///   - Count aggregate comparison
///   - nested relationship predicates
///   - ConnectRelationship / DisconnectRelationship
///   - SetField effects
///
/// Provider-specific plan shape, correlation carriers and SQL are intentionally
/// not asserted here. Those belong to the next measurement gate.
/// </summary>
public sealed class ComplexSemanticMutationE2ETests
{
    // Internal (not private): downstream stateful integration tests — e.g.
    // QueryMutationQueryMutationIntegrationE2ETests — reference
    // these identifiers directly instead of restating magic EntityId/FieldId
    // literals that would silently drift out of sync with BuildGraph() below.
    internal static readonly EntityId Customer = new(700);
    internal static readonly EntityId Profile = new(701);
    internal static readonly EntityId Account = new(702);
    internal static readonly EntityId Order = new(703);
    internal static readonly EntityId Payment = new(704);
    internal static readonly EntityId Audit = new(705);

    internal static readonly RelationshipId CustomerProfiles = new(1);
    internal static readonly RelationshipId CustomerAccounts = new(2);
    internal static readonly RelationshipId AccountOrders = new(3);
    internal static readonly RelationshipId OrderPayments = new(4);
    internal static readonly RelationshipId CustomerAudits = new(5);
    internal static readonly RelationshipId AuditCustomer = new(6);

    internal static readonly FieldId Id = new(1);
    internal static readonly FieldId Name = new(2);
    internal static readonly FieldId Status = new(3);
    internal static readonly FieldId CustomerId = new(4);
    internal static readonly FieldId AccountId = new(5);
    internal static readonly FieldId OrderId = new(6);
    internal static readonly FieldId Amount = new(7);
    internal static readonly FieldId Notes = new(8);
    internal static readonly FieldId Kind = new(9);

    [Fact]
    public void Complex_semantic_mutation_graph_covers_full_vocabulary_and_four_generation_value_flow()
    {
        var model = BuildSemanticModel();
        var graph = BuildGraph();

        // The static semantic topology is part of the same proof: all relationship
        // filters below must point at declared semantic relationships.
        Assert.Equal(6, model.Entities.Count);
        Assert.Equal(AccountOrders, model.Get(Account).Relationships.Single().Id);

        var plan = new SemanticMutationPlanner().Plan(graph);

        Assert.Equal(10, plan.Operations.Count);

        // Canonical value flow:
        //   0 Customer -> 2 Account -> 4 Order -> 6 Payment
        AssertDependency(plan, 0, 2, Id, CustomerId, CustomerAccounts);
        // Relationship context is preserved when it is explicitly supplied
        // by the legacy dependency declaration. Source-derived dependencies
        // remain provider-neutral value-flow edges and do not invent a
        // relationship identity.
        AssertDependency(plan, 2, 4, Id, AccountId, null);
        AssertDependency(plan, 4, 6, Id, OrderId, null);

        // Operation 2 contains a direct Source and also legacy explicit dependency
        // metadata for the same edge. The planner must produce one canonical edge
        // and retain the relationship context from the legacy declaration.
        Assert.Equal(4, plan.Dependencies.Count);

        // The remaining operations deliberately exercise independent mutation
        // semantics without introducing another value-flow edge.
        Assert.Equal(SemanticMutationKind.Create, plan.Operations[0].Kind);
        Assert.Equal(SemanticMutationKind.Create, plan.Operations[1].Kind);
        Assert.Equal(SemanticMutationKind.Upsert, plan.Operations[2].Kind);
        Assert.Equal(SemanticMutationKind.Update, plan.Operations[3].Kind);
        Assert.Equal(SemanticMutationKind.Create, plan.Operations[4].Kind);
        Assert.Equal(SemanticMutationKind.Update, plan.Operations[5].Kind);
        Assert.Equal(SemanticMutationKind.Upsert, plan.Operations[6].Kind);
        Assert.Equal(SemanticMutationKind.Delete, plan.Operations[7].Kind);
        Assert.Equal(SemanticMutationKind.Update, plan.Operations[8].Kind);
        Assert.Equal(SemanticMutationKind.Update, plan.Operations[9].Kind);

        // Upsert conflict semantics survive planning.
        Assert.Equal([CustomerId], plan.Operations[2].ConflictFields);

        // Return-field semantics survive planning for every generated-value producer.
        Assert.Contains(Id, plan.Operations[0].ReturnFields);
        Assert.Contains(Id, plan.Operations[2].ReturnFields);
        Assert.Contains(Id, plan.Operations[4].ReturnFields);
        Assert.Contains(Id, plan.Operations[6].ReturnFields);

        // Complex filters remain semantic expressions; no SQL/provider artifact
        // is introduced by planning.
        Assert.IsType<SemanticAndFilter>(plan.Operations[3].Filter);
        Assert.IsType<SemanticAndFilter>(plan.Operations[5].Filter);
        Assert.IsType<SemanticOrFilter>(plan.Operations[7].Filter);
        Assert.IsType<SemanticAndFilter>(plan.Operations[9].Filter);

        // Effect vocabulary is represented explicitly.
        var effects = plan.Operations.SelectMany(x => x.Effects).ToArray();
        Assert.Contains(effects, e => e.Kind == SemanticMutationEffectKind.CreateEntity);
        Assert.Contains(effects, e => e.Kind == SemanticMutationEffectKind.UpdateEntity);
        Assert.Contains(effects, e => e.Kind == SemanticMutationEffectKind.DeleteEntity);
        Assert.Contains(effects, e => e.Kind == SemanticMutationEffectKind.UpsertEntity);
        Assert.Contains(effects, e => e.Kind == SemanticMutationEffectKind.SetField);
        Assert.Contains(effects, e => e.Kind == SemanticMutationEffectKind.ConnectRelationship);
        Assert.Contains(effects, e => e.Kind == SemanticMutationEffectKind.DisconnectRelationship);
    }

    [Fact]
    public void Complex_semantic_graph_rejects_forward_dependency_even_when_graph_is_otherwise_valid()
    {
        var operations = BuildGraph().Operations.ToArray();

        // Operation 0 cannot depend on operation 2 because value flow is forward-only.
        operations[0] = operations[0] with
        {
            Dependencies =
            [
                new SemanticMutationDependency(2, 0, Id, Name)
            ]
        };

        var invalid = new SemanticMutationOperationGraph(operations);

        var exception = Assert.Throws<InvalidOperationException>(() => new SemanticMutationPlanner().Plan(invalid));

        Assert.Contains("earlier operation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Complex_semantic_graph_rejects_dependency_to_field_not_returned_by_source()
    {
        var operations = BuildGraph().Operations.ToArray();

        // Customer operation 0 returns Id/Name/Status, but not Notes.
        operations[2] = operations[2] with
        {
            Fields =
            [
                new SemanticMutationField(
                    CustomerId,
                    null,
                    new SemanticMutationValueReference(0, Notes))
            ]
        };

        var invalid = new SemanticMutationOperationGraph(operations);

        var exception = Assert.Throws<InvalidOperationException>(() => new SemanticMutationPlanner().Plan(invalid));

        Assert.Contains("does not return that field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    internal static SemanticModel BuildSemanticModel()
    {
        return new SemanticModelBuilder()
            .Entity(Customer, "Customer", e => e
                .Identity(Id, "Id")
                .Field(Name, "Name", typeof(string))
                .Field(Status, "Status", typeof(string))
                .Field(Notes, "Notes", typeof(string))
                .Relationship(CustomerProfiles, "profiles", Profile, RelationshipCardinality.Many)
                .Relationship(CustomerAccounts, "accounts", Account, RelationshipCardinality.Many)
                .Relationship(CustomerAudits, "audits", Audit, RelationshipCardinality.Many))
            .Entity(Profile, "Profile", e => e
                .Identity(Id, "Id")
                .Field(CustomerId, "CustomerId", typeof(long))
                .Field(Name, "Name", typeof(string)))
            .Entity(Account, "Account", e => e
                .Identity(Id, "Id")
                .Field(CustomerId, "CustomerId", typeof(long))
                .Field(Name, "Name", typeof(string))
                .Field(Status, "Status", typeof(string))
                .Relationship(AccountOrders, "orders", Order, RelationshipCardinality.Many))
            .Entity(Order, "Order", e => e
                .Identity(Id, "Id")
                .Field(AccountId, "AccountId", typeof(long))
                .Field(Status, "Status", typeof(string))
                .Relationship(OrderPayments, "payments", Payment, RelationshipCardinality.Many))
            .Entity(Payment, "Payment", e => e
                .Identity(Id, "Id")
                .Field(OrderId, "OrderId", typeof(long))
                .Field(Amount, "Amount", typeof(decimal))
                .Field(Status, "Status", typeof(string)))
            .Entity(Audit, "Audit", e => e
                .Identity(Id, "Id")
                .Field(CustomerId, "CustomerId", typeof(long))
                .Field(Kind, "Kind", typeof(string))
                .Relationship(AuditCustomer, "customer", Customer, RelationshipCardinality.One))
            .Build();
    }

    internal static SemanticMutationOperationGraph BuildGraph()
    {
        var operations = new SemanticMutationOperation[]
        {
            // 0: Create Customer. This is the root generated identity.
            new(
                Customer,
                SemanticMutationKind.Create,
                [
                    new SemanticMutationField(Name, "Alice"),
                    new SemanticMutationField(Status, "Active"),
                    new SemanticMutationField(Notes, "postgres-e2e")
                ],
                Filter: null,
                ConflictFields: [],
                ReturnFields: [Id, Name, Status],
                Effects:
                [
                    new(SemanticMutationEffectKind.CreateEntity, Customer),
                    new(SemanticMutationEffectKind.SetField, Customer, Name),
                    new(SemanticMutationEffectKind.SetField, Customer, Status),
                    new(SemanticMutationEffectKind.SetField, Customer, Notes)
                ],
                Dependencies: []),

            // 1: Create Profile. Independent child mutation using a generated
            // customer identity but deliberately not part of the four-generation
            // chain, so the graph contains multiple branches.
            new(
                Profile,
                SemanticMutationKind.Create,
                [
                    new SemanticMutationField(
                        CustomerId,
                        null,
                        new SemanticMutationValueReference(0, Id)),
                    new SemanticMutationField(Name, "Primary")
                ],
                Filter: null,
                ConflictFields: [],
                ReturnFields: [Id, CustomerId],
                Effects:
                [
                    new(SemanticMutationEffectKind.CreateEntity, Profile),
                    new(SemanticMutationEffectKind.SetField, Profile, CustomerId),
                    new(SemanticMutationEffectKind.SetField, Profile, Name)
                ],
                Dependencies: []),

            // 2: Upsert Account. The Source is canonical. The explicit dependency
            // is retained only as compatibility/context metadata for the same edge.
            new(
                Account,
                SemanticMutationKind.Upsert,
                [
                    new SemanticMutationField(
                        CustomerId,
                        null,
                        new SemanticMutationValueReference(0, Id)),
                    new SemanticMutationField(Name, "Primary"),
                    new SemanticMutationField(Status, "Open")
                ],
                Filter: null,
                ConflictFields: [CustomerId],
                ReturnFields: [Id, CustomerId, Status],
                Effects:
                [
                    new(SemanticMutationEffectKind.UpsertEntity, Account),
                    new(SemanticMutationEffectKind.SetField, Account, CustomerId),
                    new(SemanticMutationEffectKind.SetField, Account, Name),
                    new(SemanticMutationEffectKind.SetField, Account, Status)
                ],
                Dependencies:
                [
                    new(0, 2, Id, CustomerId, CustomerAccounts)
                ]),

            // 3: Update Customer with AND/OR, IN/NEQ, relationship Some/None
            // and an aggregate Count comparison.
            new(
                Customer,
                SemanticMutationKind.Update,
                [
                    new SemanticMutationField(Status, "Verified")
                ],
                new SemanticAndFilter(
                [
                    new SemanticFieldFilter(Status, SemanticFilterOperator.Neq, "Blocked"),
                    new SemanticOrFilter(
                    [
                        new SemanticFieldFilter(
                            Status,
                            SemanticFilterOperator.In,
                            new[] { "Active", "Pending", "Verified" }),
                        new SemanticRelationshipFilter(
                            CustomerProfiles,
                            SemanticRelationshipQuantifier.Some,
                            new SemanticFieldFilter(Name, SemanticFilterOperator.Eq, "Primary")),
                        new SemanticRelationshipFilter(
                            CustomerAudits,
                            SemanticRelationshipQuantifier.None,
                            new SemanticFieldFilter(Kind, SemanticFilterOperator.Eq, "Security"))
                    ]),
                    new SemanticAggregateFilter(
                        CustomerAccounts,
                        SemanticFilterAggregate.Count,
                        null,
                        SemanticAggregateFilterOperator.Gte,
                        1)
                ]),
                ConflictFields: [],
                ReturnFields: [Id, Status],
                Effects:
                [
                    new(SemanticMutationEffectKind.UpdateEntity, Customer),
                    new(SemanticMutationEffectKind.SetField, Customer, Status)
                ],
                Dependencies: []),

            // 4: Create Order. Second generation in the value-flow chain.
            new(
                Order,
                SemanticMutationKind.Create,
                [
                    new SemanticMutationField(
                        AccountId,
                        null,
                        new SemanticMutationValueReference(2, Id)),
                    new SemanticMutationField(Status, "Pending")
                ],
                Filter: null,
                ConflictFields: [],
                ReturnFields: [Id, AccountId],
                Effects:
                [
                    new(SemanticMutationEffectKind.CreateEntity, Order),
                    new(SemanticMutationEffectKind.SetField, Order, AccountId),
                    new(SemanticMutationEffectKind.SetField, Order, Status)
                ],
                Dependencies: []),

            // 5: Update Order with None/All relationship predicates and nested
            // OR composition.
            new(
                Order,
                SemanticMutationKind.Update,
                [
                    new SemanticMutationField(Status, "Ready")
                ],
                new SemanticAndFilter(
                [
                    new SemanticRelationshipFilter(
                        OrderPayments,
                        SemanticRelationshipQuantifier.None,
                        new SemanticFieldFilter(Status, SemanticFilterOperator.Eq, "Failed")),
                    new SemanticRelationshipFilter(
                        OrderPayments,
                        SemanticRelationshipQuantifier.All,
                        new SemanticOrFilter(
                        [
                            new SemanticFieldFilter(Status, SemanticFilterOperator.Eq, "Captured"),
                            new SemanticFieldFilter(Status, SemanticFilterOperator.Eq, "Pending")
                        ]))
                ]),
                ConflictFields: [],
                ReturnFields: [Id, Status],
                Effects:
                [
                    new(SemanticMutationEffectKind.UpdateEntity, Order),
                    new(SemanticMutationEffectKind.SetField, Order, Status)
                ],
                Dependencies: []),

            // 6: Upsert Payment. Third/fourth generation chain link.
            new(
                Payment,
                SemanticMutationKind.Upsert,
                [
                    new SemanticMutationField(
                        OrderId,
                        null,
                        new SemanticMutationValueReference(4, Id)),
                    new SemanticMutationField(Amount, 250m),
                    new SemanticMutationField(Status, "Pending")
                ],
                Filter: null,
                ConflictFields: [OrderId],
                ReturnFields: [Id, OrderId, Amount],
                Effects:
                [
                    new(SemanticMutationEffectKind.UpsertEntity, Payment),
                    new(SemanticMutationEffectKind.SetField, Payment, OrderId),
                    new(SemanticMutationEffectKind.SetField, Payment, Amount),
                    new(SemanticMutationEffectKind.SetField, Payment, Status)
                ],
                Dependencies: []),

            // 7: Delete Audit with nested AND/OR and relationship None.
            new(
                Audit,
                SemanticMutationKind.Delete,
                [],
                new SemanticOrFilter(
                [
                    new SemanticAndFilter(
                    [
                        new SemanticFieldFilter(Kind, SemanticFilterOperator.Eq, "Temporary"),
                        new SemanticFieldFilter(CustomerId, SemanticFilterOperator.In, new[] { 1L, 2L, 3L })
                    ]),
                    new SemanticRelationshipFilter(
                        AuditCustomer,
                        SemanticRelationshipQuantifier.None,
                        new SemanticFieldFilter(Status, SemanticFilterOperator.Neq, "Blocked"))
                ]),
                ConflictFields: [],
                ReturnFields: [Id, CustomerId],
                Effects:
                [
                    new(SemanticMutationEffectKind.DeleteEntity, Audit)
                ],
                Dependencies: []),

            // 8: Update Account and exercise relationship effects.
            new(
                Account,
                SemanticMutationKind.Update,
                [
                    new SemanticMutationField(Status, "Active")
                ],
                new SemanticAggregateFilter(
                    AccountOrders,
                    SemanticFilterAggregate.Count,
                    null,
                    SemanticAggregateFilterOperator.Gt,
                    0),
                ConflictFields: [],
                ReturnFields: [Id, Status],
                Effects:
                [
                    new(SemanticMutationEffectKind.UpdateEntity, Account),
                    new(SemanticMutationEffectKind.SetField, Account, Status),
                    new(SemanticMutationEffectKind.ConnectRelationship, Account, Relationship: AccountOrders),
                    new(SemanticMutationEffectKind.DisconnectRelationship, Account, Relationship: AccountOrders)
                ],
                Dependencies: []),

            // 9: Final Customer update with nested relationship semantics. It also
            // carries a second explicit dependency to prove multiple graph
            // operations can coexist while preserving canonical source identity.
            new(
                Customer,
                SemanticMutationKind.Update,
                [
                    new SemanticMutationField(Notes, "relationship-effects")
                ],
                new SemanticAndFilter(
                [
                    new SemanticRelationshipFilter(
                        CustomerAccounts,
                        SemanticRelationshipQuantifier.Some,
                        new SemanticAndFilter(
                        [
                            new SemanticFieldFilter(Status, SemanticFilterOperator.Eq, "Active"),
                            new SemanticRelationshipFilter(
                                AccountOrders,
                                SemanticRelationshipQuantifier.Some,
                                new SemanticFieldFilter(Status, SemanticFilterOperator.Eq, "Pending"))
                        ])),
                    new SemanticRelationshipFilter(
                        CustomerAudits,
                        SemanticRelationshipQuantifier.All,
                        new SemanticOrFilter(
                        [
                            new SemanticFieldFilter(Kind, SemanticFilterOperator.Eq, "Security"),
                            new SemanticFieldFilter(Kind, SemanticFilterOperator.Eq, "Compliance")
                        ]))
                ]),
                ConflictFields: [],
                ReturnFields: [Id, Notes],
                Effects:
                [
                    new(SemanticMutationEffectKind.UpdateEntity, Customer),
                    new(SemanticMutationEffectKind.SetField, Customer, Notes),
                    new(SemanticMutationEffectKind.ConnectRelationship, Customer, Relationship: CustomerAccounts),
                    new(SemanticMutationEffectKind.DisconnectRelationship, Customer, Relationship: CustomerAccounts)
                ],
                Dependencies: [])
        };

        return new SemanticMutationOperationGraph(operations);
    }

    private static void AssertDependency(
        SemanticMutationPlan plan,
        int from,
        int to,
        FieldId sourceField,
        FieldId targetField,
        RelationshipId? relationship)
    {
        var dependency = Assert.Single(
            plan.Dependencies, x =>
                x.FromOperationId == from.ToString() &&
                x.ToOperationId == to.ToString() &&
                x.SourceField == sourceField &&
                x.TargetField == targetField);

        Assert.Equal(relationship, dependency.Relationship);
    }
}