using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticAuthorizationTests
{
    [Fact]
    public void Contract_aware_authorization_accepts_operation_from_same_contract()
    {
        var (model, request, customer, _, _) = CreateBankingRequest();
        var contract = model.Freeze().CreateSnapshot();
        var operation = Foundgine.Core.Semantic.IR.SemanticOperationCompiler.Compile(
            new SemanticRequestResolver(contract).Resolve(request));

        var authorized = new SemanticAuthorizer(new DenyAccountPolicy()).Authorize(contract, operation);

        Assert.Equal(customer, authorized.Root.EntityId);
    }

    [Fact]
    public void Contract_aware_authorization_rejects_unknown_entity_before_policy_evaluation()
    {
        var (model, request, _, _, _) = CreateBankingRequest();
        var contract = model.Freeze().CreateSnapshot();
        var operation = Foundgine.Core.Semantic.IR.SemanticOperationCompiler.Compile(
            new SemanticRequestResolver(contract).Resolve(request));
        operation = operation with { Root = operation.Root with { EntityId = new EntityId(999) } };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(contract, operation));

        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Contract_aware_authorization_rejects_relationship_target_mismatch_before_policy_evaluation()
    {
        var (model, request, _, account, _) = CreateBankingRequest();
        var contract = model.Freeze().CreateSnapshot();
        var operation = Foundgine.Core.Semantic.IR.SemanticOperationCompiler.Compile(
            new SemanticRequestResolver(contract).Resolve(request));
        var child = operation.Root.Children.Single();
        operation = operation with
        {
            Root = operation.Root with
            {
                Children = [child with { EntityId = new EntityId(1) }]
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).Authorize(contract, operation));

        Assert.Contains("targets", ex.Message);
        Assert.Equal(account, child.EntityId);
    }

    [Fact]
    public void Authorization_can_be_applied_to_canonical_semantic_ir()
    {
        var (model, request, customer, account, _) = CreateBankingRequest();
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var operation = Foundgine.Core.Semantic.IR.SemanticOperationCompiler.Compile(resolved);

        var authorized = new SemanticAuthorizer(new DenyAccountPolicy()).Authorize(operation);

        Assert.Equal(customer, authorized.Root.EntityId);
        Assert.Empty(authorized.Root.Children);
    }

    [Fact]
    public void Conditional_authorization_is_preserved_when_authorizing_semantic_ir()
    {
        var predicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ContextParameter("user"), "TenantId"));

        var (model, request, _, _, _) = CreateBankingRequest();
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var operation = Foundgine.Core.Semantic.IR.SemanticOperationCompiler.Compile(resolved);

        var authorized = new SemanticAuthorizer(new ConditionalPolicy(predicate)).Authorize(operation);

        Assert.Equal(predicate, authorized.Root.Authorization);
    }

    [Fact]
    public void Denied_field_is_removed_from_authorized_graph()
    {
        var (model, request, customer, account, _) = CreateBankingRequest();

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new DenyBalancePolicy()).Authorize(resolved);

        Assert.Equal(3, authorized.Nodes.Count);
        Assert.Equal(new[] { new FieldId(1), new FieldId(2) }, authorized.Nodes[0].Fields);
        Assert.Equal(new[] { new FieldId(1) }, authorized.Nodes[1].Fields);
        Assert.DoesNotContain(new FieldId(3), authorized.Nodes[1].Fields);
        Assert.Equal(account, authorized.Nodes[1].EntityId);
    }

    [Fact]
    public void Denying_every_requested_field_does_not_reintroduce_fields()
    {
        var (model, request, _, _, _) = CreateBankingRequest();
        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new DenyAllCustomerFieldsPolicy()).Authorize(resolved);

        Assert.Empty(authorized.Nodes[0].Fields);
    }

    [Fact]
    public void Denied_relationship_removes_relationship_subtree()
    {
        var (model, request, customer, _, transaction) = CreateBankingRequest();

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new DenyTransactionsPolicy()).Authorize(resolved);

        Assert.Equal(2, authorized.Nodes.Count);
        Assert.Equal(customer, authorized.Nodes[0].EntityId);
        Assert.Equal(new EntityId(2), authorized.Nodes[1].EntityId);
        Assert.DoesNotContain(authorized.Nodes, node => node.EntityId == transaction);
    }

    [Fact]
    public void Denied_child_entity_removes_that_subtree()
    {
        var (model, request, customer, account, _) = CreateBankingRequest();

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new DenyAccountPolicy()).Authorize(resolved);

        Assert.Single(authorized.Nodes);
        Assert.Equal(customer, authorized.Nodes[0].EntityId);
        Assert.DoesNotContain(authorized.Nodes, node => node.EntityId == account);
    }

    [Fact]
    public void Denied_root_entity_rejects_request()
    {
        var (model, request, _, _, _) = CreateBankingRequest();

        var resolved = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);

        var ex = Assert.Throws<SemanticAuthorizationException>(() =>
            new SemanticAuthorizer(new DenyCustomerPolicy()).Authorize(resolved));

        Assert.Contains("Access denied", ex.Message);
    }

    private static (SemanticModel Model, SemanticRequest Request, EntityId Customer, EntityId Account, EntityId
        Transaction)
        CreateBankingRequest()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var transaction = new EntityId(3);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Balance", typeof(decimal))
                .Relationship(new RelationshipId(2), "Transactions", transaction, RelationshipCardinality.Many))
            .Entity(transaction, "Transaction", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Amount", typeof(decimal)))
            .Build();

        var request = new SemanticRequest(
            customer,
            [
                new SemanticSelection(new FieldId(1), null, []),
                new SemanticSelection(new FieldId(2), null, []),
                new SemanticSelection(
                    null,
                    new RelationshipId(1),
                    [
                        new SemanticSelection(new FieldId(1), null, []),
                        new SemanticSelection(new FieldId(3), null, []),
                        new SemanticSelection(
                            null,
                            new RelationshipId(2),
                            [new SemanticSelection(new FieldId(1), null, [])])
                    ])
            ]);

        return (model, request, customer, account, transaction);
    }

    private sealed class DenyBalancePolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) =>
            entityId != new EntityId(2) || fieldId != new FieldId(3);
    }

    private sealed class DenyAllCustomerFieldsPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) =>
            entityId != new EntityId(1);
    }

    private sealed class DenyTransactionsPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) =>
            sourceEntityId != new EntityId(2) || relationshipId != new RelationshipId(2);
    }

    private sealed class DenyAccountPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => entityId != new EntityId(2);
    }

    private sealed class ConditionalPolicy(AuthorizationPredicate predicate) : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(
            EntityId entityId,
            AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read ? predicate : null;
    }

    private sealed class DenyCustomerPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => entityId != new EntityId(1);
    }
}

// : capability discovery and conditional authorization are semantic
// concerns. They are intentionally tested without SQL or GraphQL.
public sealed class SemanticAuthorizationCapabilityTests
{
    [Fact]
    public void Capability_discovery_reports_read_write_field_boundaries()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Employee", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Field(new FieldId(3), "Salary", typeof(decimal)))
            .Build();

        var capabilities = SemanticAuthorizationCapabilityDiscovery.Describe(
            model,
            new EmployeePolicy());

        var employee = Assert.Single(capabilities.Entities);
        Assert.Equal(AuthorizationAccess.Allowed, employee.Read.Access);
        Assert.Equal(AuthorizationAccess.Denied, employee.Write.Access);

        var name = Assert.Single(employee.Fields, x => x.Name == "Name");
        Assert.Equal(AuthorizationAccess.Allowed, name.Read.Access);
        Assert.Equal(AuthorizationAccess.Denied, name.Write.Access);

        var salary = Assert.Single(employee.Fields, x => x.Name == "Salary");
        Assert.Equal(AuthorizationAccess.Denied, salary.Read.Access);
    }

    [Fact]
    public void Capability_discovery_reports_conditional_without_exposing_predicate()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Employee", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "TenantId", typeof(int)))
            .Build();

        var capabilities = SemanticAuthorizationCapabilityDiscovery.Describe(model, new TenantPolicy());
        var employee = Assert.Single(capabilities.Entities);

        Assert.Equal(AuthorizationAccess.Conditional, employee.Read.Access);
        Assert.Null(employee.Read.Predicate);
    }

    [Fact]
    public void Conditional_policy_predicate_is_preserved_in_authorized_graph()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Employee", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "TenantId", typeof(int)))
            .Build();

        var request = new SemanticRequest(
            new EntityId(1),
            [new SemanticSelection(new FieldId(2), null, [])]);

        var graph = new SemanticRequestResolver(model.Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(new TenantPolicy()).Authorize(graph);

        var predicate = Assert.Single(authorized.Nodes).Authorization;
        Assert.NotNull(predicate);
        Assert.Equal(AuthorizationPredicateKind.Equal, predicate.Kind);
        Assert.Equal(AuthorizationPredicateKind.MemberAccess, predicate.Left?.Kind);
        Assert.Equal(AuthorizationPredicateKind.ContextParameter, predicate.Right?.Left?.Kind);
    }

    private sealed class EmployeePolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanWriteEntity(EntityId entityId) => false;

        public override bool CanWriteField(EntityId entityId, FieldId fieldId) => false;

        public override bool CanAccessField(EntityId entityId, FieldId fieldId) =>
            entityId != new EntityId(1) || fieldId != new FieldId(3);
    }

    private sealed class TenantPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(
            EntityId entityId,
            AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read && entityId == new EntityId(1)
                ? AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.ContextParameter("context"), "TenantId"))
                : null;
    }
}