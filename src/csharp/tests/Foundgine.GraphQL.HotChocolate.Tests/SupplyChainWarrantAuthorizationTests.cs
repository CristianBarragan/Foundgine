using System.Security.Claims;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Keycloak.Warrants;
using Foundgine.SupplyChain.BenchmarkApi;
using Foundgine.SupplyChain.Domain;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Foundgine.GraphQL.HotChocolate.Tests;

public sealed class SupplyChainWarrantAuthorizationTests
{
    [Fact]
    public void Customer_warrant_is_bound_to_its_issued_tenant()
    {
        using var keys = PemWarrantSigningKeyProvider.CreateEphemeral("test-key");
        var options = new FoundgineKeycloakOptions();
        options.RoleGrants["sc-customer"] =
        [
            new FoundgineKeycloakGrant { Capability = "SalesOrder.read", Operation = "read" }
        ];
        var context = new FoundgineKeycloakWarrantIssuer(options, keys)
            .Issue("alice", "tenant-a", ["sc-customer"]);

        Assert.NotNull(context);
        Assert.True(SecurityWarrantAuthorization.Allows(
            context.Warrant, context.Subject, context.Audience, "SalesOrder.read", "read",
            "tenant-a", "tenant-a"));
        Assert.False(SecurityWarrantAuthorization.Allows(
            context.Warrant, context.Subject, context.Audience, "SalesOrder.read", "read",
            "tenant-b", "tenant-b"));
    }

    [Fact]
    public void Analyst_warrant_does_not_inherit_customer_or_operator_capabilities()
    {
        using var keys = PemWarrantSigningKeyProvider.CreateEphemeral("test-key");
        var options = new FoundgineKeycloakOptions();
        options.RoleGrants["sc-analyst"] =
        [
            new FoundgineKeycloakGrant { Capability = "Supplier.read", Operation = "read" }
        ];
        var context = new FoundgineKeycloakWarrantIssuer(options, keys)
            .Issue("analyst", "tenant-a", ["sc-analyst"]);

        Assert.NotNull(context);
        Assert.True(SecurityWarrantAuthorization.Allows(
            context.Warrant, context.Subject, context.Audience, "Supplier.read", "read",
            "tenant-a", "tenant-a"));
        Assert.False(SecurityWarrantAuthorization.Allows(
            context.Warrant, context.Subject, context.Audience, "Shipment.update", "update",
            "tenant-a", "tenant-a"));
    }

    [Fact]
    public void Customer_sales_order_read_uses_a_bound_owner_predicate()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "sc-customer"), new Claim("customer_id", "42")], "test"));
        var policy = new WarrantSemanticAuthorizationPolicy(new HttpContextAccessor { HttpContext = http });

        var predicate = policy.GetPredicate(
            SupplyChainSemantics.SalesOrder.Entity, AuthorizationOperation.Read);

        Assert.NotNull(predicate);
        Assert.Equal(AuthorizationPredicateKind.Equal, predicate.Kind);
        Assert.Equal(AuthorizationPredicateKind.MemberAccess, predicate.Left?.Kind);
        Assert.Equal(AuthorizationPredicateKind.ResourceParameter, predicate.Left?.Left?.Kind);
        Assert.Equal("CustomerId", predicate.Left?.Name);
        Assert.Equal(AuthorizationPredicateKind.MemberAccess, predicate.Right?.Kind);
        Assert.Equal(AuthorizationPredicateKind.ContextParameter, predicate.Right?.Left?.Kind);
        Assert.Equal("CustomerId", predicate.Right?.Name);
    }

    [Fact]
    public void Customer_without_customer_id_gets_a_fail_closed_owner_predicate()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "sc-customer")], "test"));
        var policy = new WarrantSemanticAuthorizationPolicy(new HttpContextAccessor { HttpContext = http });

        var predicate = policy.GetPredicate(
            SupplyChainSemantics.SalesOrder.Entity, AuthorizationOperation.Read);

        Assert.NotNull(predicate);
        Assert.Equal(AuthorizationPredicateKind.Equal, predicate.Kind);
        Assert.Equal("-1", predicate.Right?.Value);
    }

    [Fact]
    public void Operator_does_not_receive_customer_owner_predicate()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "sc-operator")], "test"));
        var policy = new WarrantSemanticAuthorizationPolicy(new HttpContextAccessor { HttpContext = http });

        Assert.Null(policy.GetPredicate(
            SupplyChainSemantics.SalesOrder.Entity, AuthorizationOperation.Read));
    }
}
