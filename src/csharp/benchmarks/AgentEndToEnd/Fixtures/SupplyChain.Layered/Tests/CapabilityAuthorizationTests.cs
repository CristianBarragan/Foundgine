using Foundgine.SupplyChain.Application;
using Xunit;

namespace Foundgine.SupplyChain.Tests;

/// <summary>
/// GUIDE.md's testing-layer progression lists "capability authorization
/// tests" first, ahead of AOT metadata/semantic plan/SQL compilation
/// coverage. <see cref="AotAndPlanningTests"/> only exercises the metadata
/// and planning layers; this fixture covers <see cref="SupplyChainAuthorizer"/>
/// in isolation so the demand/authenticate boundary described in
/// Application/Authorization.cs has its own regression tests.
/// </summary>
public sealed class CapabilityAuthorizationTests
{
    private const string AliceToken = "alice-demo-token";
    private const string BobToken = "bob-demo-token";
    private const string AdminToken = "admin-demo-token";

    private static readonly ICapabilityAuthorizer Authorizer = new SupplyChainAuthorizer();

    [Fact]
    public void No_credentials_are_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(() => Authorizer.Authenticate("", ""));
        Assert.Throws<UnauthorizedAccessException>(() => Authorizer.Authenticate("alice", ""));
    }

    [Fact]
    public void Wrong_token_for_a_real_actor_is_rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(() => Authorizer.Authenticate("alice", "not-alices-token"));
    }

    [Fact]
    public void Unknown_actor_and_known_actor_with_wrong_token_give_identical_errors()
    {
        var unknownActor = Record.Exception(() => Authorizer.Authenticate("eve", "anything"));
        var wrongToken = Record.Exception(() => Authorizer.Authenticate("alice", "wrong"));

        Assert.NotNull(unknownActor);
        Assert.NotNull(wrongToken);
        Assert.Equal(unknownActor.Message, wrongToken.Message);
    }

    [Fact]
    public void Actor_without_the_capability_is_denied()
    {
        // "alice" is a customer-facing actor; she has no warehouse capability.
        Assert.Throws<UnauthorizedAccessException>(() =>
            Authorizer.Demand("alice", AliceToken, "update_inventory"));
    }

    [Fact]
    public void Actor_can_act_on_their_own_customer_scoped_resource()
    {
        // "alice" is mapped to customerId 1 server-side.
        Authorizer.Demand("alice", AliceToken, "get_order", customerId: 1);
    }

    [Fact]
    public void Actor_cannot_act_on_another_actors_customer_scoped_resource()
    {
        // "alice" is customer 1; asking for customer 2's order must be denied
        // even though "alice" otherwise has the "get_order" capability.
        Assert.Throws<UnauthorizedAccessException>(() =>
            Authorizer.Demand("alice", AliceToken, "get_order", customerId: 2));
    }

    [Fact]
    public void Admin_may_act_across_customers()
    {
        Authorizer.Demand("admin", AdminToken, "get_order", customerId: 1);
        Authorizer.Demand("admin", AdminToken, "get_order", customerId: 2);
    }

    [Fact]
    public void Non_customer_scoped_capability_does_not_require_customer_ownership()
    {
        // "bob" has "list_customers", which is not in CustomerScopedCapabilities,
        // so no ownership check applies regardless of the customerId supplied.
        Authorizer.Demand("bob", BobToken, "list_customers", customerId: 999);
    }

    [Fact]
    public void Demand_authenticates_before_checking_capability()
    {
        // Wrong credentials must fail even for a capability the actor would
        // otherwise be allowed to use, and must fail with the authentication
        // error rather than an authorization error.
        var exception = Assert.Throws<UnauthorizedAccessException>(() =>
            Authorizer.Demand("alice", "wrong-token", "get_order"));

        Assert.Contains("credentials", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
