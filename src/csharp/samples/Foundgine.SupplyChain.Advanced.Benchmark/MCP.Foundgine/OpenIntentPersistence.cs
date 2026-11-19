using System.Reflection;
using Foundgine.Generated;
using Foundgine.Generated.Orm;
using Foundgine.Providers.Aot;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Foundgine.SupplyChain.Advanced.OpenIntent;

/// <summary>
/// EF Core wiring for the open-intent sample, on the provider's one-line registration. The EF model and the ORM
/// maps both derive from the generated metadata, so tables and columns have one source of truth. This replaces the
/// hand-written DbContext / registry / execution provider the sample used to carry. Reflection-based EF model by
/// design: this sample is not Native AOT.
/// </summary>
public static class OpenIntentPersistence
{
    public static IServiceCollection AddOpenIntentPersistence(this IServiceCollection services, NpgsqlDataSource dataSource) =>
        services.AddFoundgineEf(
            GeneratedMetadata.Build(),
            o => o.UseNpgsql(dataSource),
            options => options
                .AddMaps(GeneratedOrmMaps.CreateAll())   // generated, strongly typed maps
                .MapClrTypes(ClrTypesByEntityName())     // generated metadata carries no CLR type
                .UseEngineProvider());                   // IProviderPlanCompiler + singleton IExecutionProvider

    /// <summary>The [FoundgineEntity] types of this assembly by entity name.</summary>
    private static IEnumerable<KeyValuePair<string, Type>> ClrTypesByEntityName() =>
        typeof(OpenIntentPersistence).Assembly.GetTypes()
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<FoundgineEntityAttribute>()))
            .Where(x => x.Attribute is not null)
            .Select(x => KeyValuePair.Create(x.Attribute!.Name ?? x.Type.Name, x.Type));
}
