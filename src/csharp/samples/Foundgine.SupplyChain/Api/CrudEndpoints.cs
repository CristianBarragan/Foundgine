#pragma warning disable IL2026, IL3050 // name-addressed entities resolve closed generics at run time (not an AOT sample)
using System.Text.Json;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain.Application;
using Microsoft.EntityFrameworkCore;

namespace Foundgine.SupplyChain.Api;

/// <summary>
/// REST CRUD for the reference data, with no per-entity code: <c>FoundgineEntities</c> gives every entity the same
/// create / read / update / delete (reads go through the authorized semantic reader, writes through the generic
/// repository). Orders, order lines, shipments and inventory are deliberately NOT exposed here: they change only
/// through their capabilities (place_order, cancel_order, create_shipment, update_inventory).
/// Callers identify with the X-Actor and X-Token headers and need the manage_reference_data capability.
/// <code>
/// GET    /api/Supplier            GET /api/Supplier/1
/// POST   /api/Supplier            PUT /api/Supplier/1   (partial body)      DELETE /api/Supplier/1
/// </code>
/// </summary>
public static class CrudEndpoints
{
    private static readonly HashSet<string> Exposed =
        new(["Customer", "Supplier", "Category", "CatalogProduct", "Warehouse", "Carrier"], StringComparer.OrdinalIgnoreCase);

    public static IEndpointRouteBuilder MapReferenceDataCrud(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/{entity}").AddEndpointFilter(Guard);

        api.MapGet("", async (string entity, FoundgineEntities entities, CancellationToken ct) =>
            Results.Ok(await entities.Entity(entity).ReadAsync(ct: ct)));

        api.MapGet("{id:int}", async (string entity, int id, FoundgineEntities entities, CancellationToken ct) =>
        {
            var rows = await entities.Entity(entity).ReadAsync(new Dictionary<string, object?> { ["Id"] = id }, ct: ct);
            return rows.Count == 0 ? Results.NotFound() : Results.Ok(rows[0]);
        });

        api.MapPost("", async (string entity, JsonElement body, FoundgineEntities entities, CancellationToken ct) =>
            Results.Created($"/api/{entity}", await entities.Entity(entity).CreateAsync(body, ct)));

        api.MapPut("{id:int}", async (string entity, int id, Dictionary<string, JsonElement> body,
            FoundgineEntities entities, CancellationToken ct) =>
        {
            var values = body.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            return await entities.Entity(entity).UpdateAsync(id, values, ct) == 0 ? Results.NotFound() : Results.NoContent();
        });

        api.MapDelete("{id:int}", async (string entity, int id, FoundgineEntities entities, CancellationToken ct) =>
            await entities.Entity(entity).DeleteAsync(id, ct) == 0 ? Results.NotFound() : Results.NoContent());

        return app;
    }

    private static async ValueTask<object?> Guard(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var name = (string?)http.Request.RouteValues["entity"] ?? "";
        if (!Exposed.Contains(name)) return Results.NotFound();

        try
        {
            http.RequestServices.GetRequiredService<ICapabilityAuthorizer>().Demand(
                http.Request.Headers["X-Actor"].ToString(), http.Request.Headers["X-Token"].ToString(),
                "manage_reference_data");
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(new { error = "Not authorized." }, statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            return await next(ctx);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (JsonException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "The change violates a constraint (duplicate or still referenced)." }); }
    }
}
