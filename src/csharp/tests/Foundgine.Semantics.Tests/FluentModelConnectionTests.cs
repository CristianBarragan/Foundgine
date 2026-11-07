using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Configuration;
using Foundgine.Core.Semantic.Metadata;
using Xunit;

namespace Foundgine.Semantics.Tests;

public sealed class FluentModelConnectionTests
{
    private sealed class Contract { public int Id { get; set; } public string Code { get; set; } = ""; }
    private sealed class ContractRow { public int Id { get; set; } public int CodeNumber { get; set; } public string TenantId { get; set; } = ""; public bool Active { get; set; } }
    private sealed class Party { public int Id { get; set; } public object? Contract { get; set; } }
    private sealed class PartyRow { public int Id { get; set; } }
    private sealed class UserContext { public string TenantId { get; set; } = ""; public bool IsAdmin { get; set; } }

    private sealed class ContractModel : FoundgineModelConfiguration<Contract>
    {
        public override void Configure(FoundgineModelBuilder<Contract> model) => model.ToEntity("Contract");
    }

    private sealed class ContractPersistence : FoundginePersistenceConfiguration<ContractRow>
    {
        public override void Configure(FoundginePersistenceBuilder<ContractRow> model) =>
            model.ToTable("contracts").HasKey(x => x.Id);
    }

    private sealed class ContractMap : FoundgineEntityMap<Contract, ContractRow>
    {
        public override void Configure(FoundgineEntityMapBuilder<Contract, ContractRow> map) =>
            map.Property(m => m.Id).To(e => e.Id);
    }

    private sealed class PartyModel : FoundgineModelConfiguration<Party>
    {
        public override void Configure(FoundgineModelBuilder<Party> model) => model.ToEntity("Party");
    }

    private sealed class PartyPersistence : FoundginePersistenceConfiguration<PartyRow>
    {
        public override void Configure(FoundginePersistenceBuilder<PartyRow> model) =>
            model.ToTable("parties").HasKey(x => x.Id);
    }

    private sealed class PartyMap : FoundgineEntityMap<Party, PartyRow>
    {
        public override void Configure(FoundgineEntityMapBuilder<Party, PartyRow> map) =>
            map.Property(m => m.Id).To(e => e.Id);
    }

    private sealed class PartyConnections : FoundgineModelConnectionConfiguration<Party>
    {
        public override void Configure(FoundgineModelConnectionBuilder<Party> connections) =>
            connections.HasConnection<ContractRow>(x => x.Contract)
                .Maps(x => x.Id, e => e.Id)
                .Authorize<UserContext>("CanVisit", (user, contract) => user.TenantId == contract.TenantId && !contract.Active);
    }

    private sealed class ConvertingConnections : FoundgineModelConnectionConfiguration<Contract>
    {
        public override void Configure(FoundgineModelConnectionBuilder<Contract> connections) =>
            connections.HasConnection<ContractRow>(x => x.Code)
                .HasName("ByCode")
                .Maps(x => x.Code, e => e.CodeNumber, int.Parse);
    }

    private sealed class Model(params IFoundgineConfiguration[] extra) : FoundgineModel
    {
        public override void Configure(FoundgineModelBuilder model)
        {
            model.Configure<ContractModel>().Configure<ContractPersistence>().Configure<ContractMap>();
            model.Configure<PartyModel>().Configure<PartyPersistence>().Configure<PartyMap>();
            foreach (var configuration in extra)
                model.Configure(configuration);
        }
    }

    [Fact]
    public void Connection_registers_target_fields_and_connection_id()
    {
        var registry = new Model(new ConvertingConnections()).Build();

        var connection = Assert.Single(registry.Connections);
        Assert.Equal(ConnectionId.Create("Contract", "ByCode"), connection.Id);
        Assert.Equal("ByCode", connection.Name);
        Assert.Equal("Code", connection.SourceMember);
        Assert.Equal(EntityId.Create("Contract"), connection.Target);

        var field = Assert.Single(connection.Fields!);
        Assert.Equal("Code", field.SourceMember);
        Assert.Equal("CodeNumber", field.TargetMember);
        Assert.Equal(typeof(string), field.SourceType);
        Assert.Equal(typeof(int), field.TargetType);
        Assert.Equal("fluent:String->Int32", field.Converter);
    }

    [Fact]
    public void Delegate_conversion_is_registered_and_callable()
    {
        var registry = new Model(new ConvertingConnections()).Build();

        var conversion = registry.FindConversion(typeof(string), typeof(int));
        Assert.NotNull(conversion);
        Assert.Equal(42, conversion!.Convert!("42"));
    }

    [Fact]
    public void Authorization_predicate_is_lowered_from_expression_tree()
    {
        var registry = new Model(new PartyConnections()).Build();

        var authorization = Assert.Single(registry.Authorizations);
        var connection = Assert.Single(registry.Connections);
        Assert.Equal(connection.Id, authorization.ConnectionId);
        Assert.Equal("CanVisit", authorization.Name);
        Assert.Equal(typeof(UserContext), authorization.ContextType);
        Assert.Equal(typeof(ContractRow), authorization.ResourceType);

        var and = authorization.Predicate!;
        Assert.Equal(AuthorizationPredicateKind.And, and.Kind);

        var equal = and.Left!;
        Assert.Equal(AuthorizationPredicateKind.Equal, equal.Kind);
        Assert.Equal(AuthorizationPredicateKind.MemberAccess, equal.Left!.Kind);
        Assert.Equal("TenantId", equal.Left.Name);
        Assert.Equal(AuthorizationPredicateKind.ContextParameter, equal.Left.Left!.Kind);
        Assert.Equal("user", equal.Left.Left.Name);
        Assert.Equal(AuthorizationPredicateKind.ResourceParameter, equal.Right!.Left!.Kind);

        var not = and.Right!;
        Assert.Equal(AuthorizationPredicateKind.Not, not.Kind);
        Assert.Equal("Active", not.Left!.Name);
    }

    [Fact]
    public void Mismatched_types_without_converter_are_reported()
    {
        var bad = new BadTypes();
        var error = Assert.Throws<InvalidOperationException>(() => new Model(bad).Build());
        Assert.Contains("differ in type", error.Message);
    }

    private sealed class BadTypes : FoundgineModelConnectionConfiguration<Contract>
    {
        public override void Configure(FoundgineModelConnectionBuilder<Contract> connections) =>
            connections.HasConnection<ContractRow>(x => x.Code)
                .Maps<object>(x => x.Code, e => e.CodeNumber);
    }

    [Fact]
    public void Unsupported_authorization_expression_is_reported()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new Model(new UnsupportedAuth()).Build());
        Assert.Contains("not supported", error.Message);
    }

    private sealed class UnsupportedAuth : FoundgineModelConnectionConfiguration<Contract>
    {
        public override void Configure(FoundgineModelConnectionBuilder<Contract> connections) =>
            connections.HasConnection<ContractRow>(x => x.Code)
                .Authorize<UserContext>("Bad", (user, row) => user.TenantId.StartsWith(row.TenantId));
    }

    [Fact]
    public void Connection_to_unmapped_entity_is_reported()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new Model(new UnmappedTarget()).Build());
        Assert.Contains("not the storage type of any entity map", error.Message);
    }

    private sealed class UnmappedTarget : FoundgineModelConnectionConfiguration<Contract>
    {
        public override void Configure(FoundgineModelConnectionBuilder<Contract> connections) =>
            connections.HasConnection<Unmapped>(x => x.Code);
    }

    private sealed class Unmapped { public int Id { get; set; } }

    [Fact]
    public void BuildInto_rejects_duplicate_connection()
    {
        var registry = new Model(new ConvertingConnections()).Build();
        var error = Assert.Throws<InvalidOperationException>(
            () => new Model(new ConvertingConnections()).BuildInto(registry));
        Assert.Contains("connection 'ByCode'", error.Message);
    }
}
