using System.Security.Cryptography;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Microsoft.AspNetCore.Http;

namespace Foundgine.SupplyChain.Advanced.OpenIntent;

/// <summary>
/// Demo-only bridge from the sample's actor/token authentication to Foundgine's
/// signed-warrant execution boundary. Production hosts should issue warrants
/// from their real identity/authorization authority rather than minting them in
/// request middleware.
/// </summary>
public static class OpenIntentDemoSecurity
{
    public const string ExpectedIssuer = "supply-chain-demo-authority";
    public const string Audience = "foundgine";
    public const string ResourceScope = "supply-chain";
    public const string KeyId = "supply-chain-demo-1";
    private const string ActorHeader = "X-Foundgine-Actor";
    private const string TokenHeader = "X-Foundgine-Token";

    private static readonly RSA SigningKey = RSA.Create(2048);

    private static readonly IReadOnlyDictionary<string, string> Tokens =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alice"] = "alice-demo-token",
            ["bob"] = "bob-demo-token",
            ["carol"] = "carol-demo-token",
            ["dave"] = "dave-demo-token",
            ["admin"] = "admin-demo-token"
        };

    public static Task PopulateSecurityContextAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(ActorHeader, out var actorValues) ||
            !context.Request.Headers.TryGetValue(TokenHeader, out var tokenValues))
            return Task.CompletedTask;

        var actor = actorValues.ToString();
        var token = tokenValues.ToString();
        if (!Authenticate(actor, token))
            return Task.CompletedTask;

        var warrant = IssueWarrant(actor);
        context.Items[typeof(SecurityExecutionContext)] = new SecurityExecutionContext(
            warrant, actor, Audience, TenantFor(actor), ResourceScope);

        return Task.CompletedTask;
    }

    public static SecurityExecutionContext? GetSecurityExecutionContext(HttpContext context) =>
        context.Items.TryGetValue(typeof(SecurityExecutionContext), out var value)
            ? value as SecurityExecutionContext
            : null;

    public sealed class KeyResolver : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string keyId) =>
            StringComparer.Ordinal.Equals(keyId, KeyId)
                ? SigningKey
                : throw new InvalidOperationException($"Unknown warrant key '{keyId}'.");
    }

    private static SecurityWarrant IssueWarrant(string actor)
    {
        var now = DateTimeOffset.UtcNow;
        var capabilities = actor.Equals("admin", StringComparison.OrdinalIgnoreCase)
            ? AllReadCapabilities()
            : actor.Equals("bob", StringComparison.OrdinalIgnoreCase)
                ? BobReadCapabilities()
                : actor.Equals("carol", StringComparison.OrdinalIgnoreCase)
                    ? CarolReadCapabilities()
                    : DaveReadCapabilities();

        var fields = actor.Equals("admin", StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<string>()
            : new[] { "Id", "FirstName", "LastName", "Email", "CustomerId", "Status", "TotalAmount",
                "OrderId", "ProductId", "Quantity", "UnitPrice", "Name", "Sku", "UnitPrice", "State",
                "TotalOrderValue", "WarehouseId", "QuantityOnHand", "ReorderLevel", "Location",
                "CarrierId", "TrackingNumber", "ExpectedDate", "ReceivedDate" };

        var constraints = new SecurityWarrantConstraints(
            allowedTenants: [TenantFor(actor)],
            allowedFields: fields,
            resourceScopes: [ResourceScope],
            allowedOperations: ["read"],
            maxResults: 100);

        return SecurityWarrantSigner.Sign(new SecurityWarrant(
            Guid.NewGuid().ToString("N"), ExpectedIssuer, actor, Audience,
            capabilities.Select(x => new CapabilityGrant(x, "read", [ResourceScope])).ToArray(),
            constraints, now.AddSeconds(-1), now.AddMinutes(5),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), KeyId, null, []), SigningKey);
    }

    private static bool Authenticate(string actor, string token) =>
        Tokens.TryGetValue(actor, out var expected) &&
        CryptographicEquals(expected, token);

    private static bool CryptographicEquals(string expected, string actual)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(expected);
        var right = System.Text.Encoding.UTF8.GetBytes(actual);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string TenantFor(string actor) => "demo";

    private static string[] AllReadCapabilities() =>
        ["Customer.read", "Order.read", "OrderItem.read", "Product.read", "Supplier.read", "Category.read",
         "Inventory.read", "Warehouse.read", "Shipment.read", "Carrier.read", "PurchaseOrder.read"];

    private static string[] BobReadCapabilities() =>
        ["Customer.read", "Order.read", "OrderItem.read", "Product.read", "Supplier.read", "Category.read",
         "Shipment.read", "Carrier.read", "PurchaseOrder.read"];

    private static string[] CarolReadCapabilities() =>
        ["Product.read", "Category.read", "Inventory.read", "Warehouse.read", "Shipment.read", "Carrier.read"];

    private static string[] DaveReadCapabilities() =>
        ["Product.read", "Category.read", "Inventory.read", "Warehouse.read", "Supplier.read"];
}
