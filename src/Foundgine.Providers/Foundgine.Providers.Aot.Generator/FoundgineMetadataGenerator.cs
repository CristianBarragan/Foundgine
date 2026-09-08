using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Foundgine.Providers.Aot;

namespace Foundgine.Providers.Aot.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class FoundgineMetadataGenerator : IIncrementalGenerator
{
    private const string EntityAttribute = "Foundgine.Providers.Aot.FoundgineEntityAttribute";
    private const string ModelAttribute = "Foundgine.Providers.Aot.FoundgineModelAttribute";
    private const string FieldAttribute = "Foundgine.Providers.Aot.FoundgineFieldAttribute";
    private const string RelationshipAttribute = "Foundgine.Providers.Aot.FoundgineRelationshipAttribute";
    private const string ConnectionAttribute = "Foundgine.Providers.Aot.FoundgineConnectionAttribute";
    private const string ConnectionMapAttribute = "Foundgine.Providers.Aot.FoundgineConnectionMapAttribute";
    private const string ModelEntityMapAttribute = "Foundgine.Providers.Aot.FoundgineModelEntityMapAttribute";
    private const string ConversionAttribute = "Foundgine.Providers.Aot.FoundgineConversionAttribute";
    private const string AuthorizationAttribute = "Foundgine.Providers.Aot.FoundgineAuthorizationAttribute";
    private const string SemanticDimensionAttribute = "Foundgine.Providers.Aot.FoundgineSemanticDimensionAttribute";
    private const string EventAttribute = "Foundgine.Providers.Aot.FoundgineEventAttribute";

#pragma warning disable RS1032 // Diagnostic messages are intentionally defined as source strings.
#pragma warning disable RS2008 // Release tracking is not required until diagnostic IDs are stabilized.

    private static readonly DiagnosticDescriptor RelationshipTargetMustBeEntity = new(
        "FGMETA001",
        "Relationship target is not a Foundgine entity",
        "Relationship '{0}.{1}' targets '{2}', but that type is not marked with [FoundgineEntity]",
        "Foundgine.Core.Semantic.Metadata",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RelationshipForeignKeyMissing = new(
        "FGMETA002",
        "Relationship foreign key property is missing",
        "Relationship '{0}.{1}' references foreign-key property '{2}', but that property does not exist on either side of the relationship",
        "Foundgine.Core.Semantic.Metadata",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RelationshipPrincipalKeyMissing = new(
        "FGMETA003",
        "Relationship principal key property is missing",
        "Relationship '{0}.{1}' references principal-key property '{2}', but that property does not exist on the principal entity '{3}'",
        "Foundgine.Core.Semantic.Metadata",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RelationshipKeyTypesMismatch = new(
        "FGMETA004",
        "Relationship key types do not match",
        "Relationship '{0}.{1}' maps foreign key '{2}' ({3}) to principal key '{4}' ({5}); the key types must match",
        "Foundgine.Core.Semantic.Metadata",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RelationshipNavigationTargetMismatch = new(
        "FGMETA005",
        "Relationship navigation target does not match",
        "Relationship '{0}.{1}' targets '{2}', but the navigation property type resolves to '{3}'",
        "Foundgine.Core.Semantic.Metadata",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RelationshipForeignKeyAmbiguous = new(
        "FGMETA006",
        "Relationship foreign key is ambiguous",
        "Relationship '{0}.{1}' finds foreign-key property '{2}' on both '{3}' and '{4}'. Declare the relationship from the side that owns the foreign key or use an unambiguous key name.",
        "Foundgine.Core.Semantic.Metadata",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RelationshipKeyMustBeScalar = new(
        "FGMETA007",
        "Relationship key must be a scalar property",
        "Relationship '{0}.{1}' uses '{2}' as a key, but that property is itself a relationship navigation. Relationship keys must reference scalar properties.",
        "Foundgine.Core.Semantic.Metadata",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidIdentityDeclaration = new(
        "FGMETA008",
        "Invalid Foundgine identity declaration",
        "{0}",
        "Foundgine.Core.Semantic.Metadata",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

#pragma warning restore RS2008

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EntityAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ModelAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var conversions = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ConversionAttribute,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => (IMethodSymbol)ctx.TargetSymbol)
            .Collect();

        var authorizations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AuthorizationAttribute,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => (IPropertySymbol)ctx.TargetSymbol)
            .Collect();

        var connectionMaps = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ConnectionMapAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var modelEntityMaps = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ModelEntityMapAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var input = entities
            .Combine(models)
            .Combine(conversions)
            .Combine(authorizations)
            .Combine(connectionMaps)
            .Combine(modelEntityMaps);

        context.RegisterSourceOutput(input, static (spc, pair) =>
        {
            var entities = pair.Left.Left.Left.Left.Left;
            var models = pair.Left.Left.Left.Left.Right;
            var conversions = pair.Left.Left.Left.Right;
            var authorizations = pair.Left.Left.Right;
            var connectionMaps = pair.Left.Right;
            var modelEntityMaps = pair.Right;

            // Do not emit a GeneratedMetadata type for projects that do not
            // contain any Foundgine AOT declarations. This keeps the runtime
            // AOT assembly free of an accidental empty generated type if the
            // analyzer is ever included transitively.
            if (entities.IsDefaultOrEmpty &&
                models.IsDefaultOrEmpty &&
                conversions.IsDefaultOrEmpty &&
                authorizations.IsDefaultOrEmpty &&
                connectionMaps.IsDefaultOrEmpty &&
                modelEntityMaps.IsDefaultOrEmpty)
            {
                return;
            }

            string? generated;
            try
            {
                generated = Emit(
                    spc,
                    entities,
                    models,
                    conversions,
                    authorizations,
                    connectionMaps,
                    modelEntityMaps);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(InvalidIdentityDeclaration, Location.None, ex.Message));
                return;
            }

            if (generated is not null)
                spc.AddSource("Foundgine.GeneratedMetadata.g.cs", generated);
        });
    }

    private static string? Emit(
        SourceProductionContext spc,
        ImmutableArray<INamedTypeSymbol> symbols,
        ImmutableArray<INamedTypeSymbol> models,
        ImmutableArray<IMethodSymbol> conversions,
        ImmutableArray<IPropertySymbol> authorizations,
        ImmutableArray<INamedTypeSymbol> connectionMaps,
        ImmutableArray<INamedTypeSymbol> modelEntityMaps)
    {
        var ordered = symbols
            .OrderBy(x => x.ToDisplayString(), StringComparer.Ordinal)
            .ToArray();

        var entityIds = AllocateEntityIds(ordered);

        if (!ValidateRelationships(spc, ordered))
            return null;

        var relationshipIds = AllocateRelationshipIds(ordered);
        var modelIds = AllocateModelIds(models);
        var connectionIds = AllocateConnectionIds(models);
        var modelEntityMap = BuildModelEntityMap(modelEntityMaps);
        var connectionMap = BuildConnectionMap(connectionMaps);

        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Foundgine.Core.Semantic.Metadata;");
        sb.AppendLine("using Foundgine.Core.Abstractions;");
        sb.AppendLine("using Foundgine.Providers.Aot;");
        sb.AppendLine("namespace Foundgine.Generated;");
        sb.AppendLine();
        sb.AppendLine("public static class GeneratedMetadata");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly MetadataRegistry Registry = Build();");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Creates the AOT-generated metadata registry for the compiled CLR model.</summary>");
        sb.AppendLine("    public static MetadataRegistry Build()");
        sb.AppendLine("    {");
        sb.AppendLine("        var registry = new MetadataRegistry();");

        foreach (var model in models.OrderBy(x => x.ToDisplayString(), StringComparer.Ordinal))
        {
            var modelId = modelIds[model.ToDisplayString()];
            var modelAttribute = GetAttribute(model, ModelAttribute);
            var modelName =
                GetNamedString(modelAttribute, "Name")
                ?? model.Name;
            var minimumWeight = GetNamedInt(modelAttribute, "MinimumWeight");
            var minimumWeightArg = minimumWeight is int mw ? mw.ToString() : "null";

            if (modelEntityMap.TryGetValue(model.ToDisplayString(), out var modelEntity) &&
                entityIds.TryGetValue(modelEntity.ToDisplayString(), out var mappedEntityId))
            {
                sb.AppendLine(
                    $"        registry.Register(new ModelMetadata(new ModelId({modelId}), \"{Escape(modelName)}\", new EntityId({mappedEntityId}), {minimumWeightArg}));");
            }
            else
            {
                sb.AppendLine(
                    $"        registry.Register(new ModelMetadata(new ModelId({modelId}), \"{Escape(modelName)}\", MinimumWeight: {minimumWeightArg}));");
            }
        }

        foreach (var entity in ordered)
        {
            var entityId = entityIds[entity.ToDisplayString()];
            var entityName = GetEntityName(entity);
            var storageName = GetEntityStorageName(entity) ?? entityName;

            var properties = entity.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p =>
                    p.DeclaredAccessibility == Accessibility.Public &&
                    !p.IsStatic)
                .ToArray();

            var scalar = properties
                .Where(p =>
                    p.GetAttributes().All(a => a.AttributeClass?.ToDisplayString() != RelationshipAttribute))
                .ToArray();

            var fieldIds = AllocateFieldIds(entity);

            var columnIds = AllocateColumnIds(entity, storageName);

            sb.AppendLine("        registry.Register(new EntityMetadata(");
            sb.AppendLine($"            new EntityId({entityId}),");
            sb.AppendLine($"            \"{Escape(entityName)}\",");
            sb.AppendLine("            new ColumnMetadata[]");
            sb.AppendLine("            {");

            foreach (var p in scalar)
            {
                var field = GetAttribute(p, FieldAttribute);

                var colName =
                    GetNamedString(field, "StorageName")
                    ?? GetNamedString(field, "Name")
                    ?? p.Name;

                var id =
                    GetNamedULong(field, "ColumnId")
                    ?? columnIds[entity.ToDisplayString() + "." + p.Name];

                sb.AppendLine(
                    $"                new ColumnMetadata(new ColumnId({id}), \"{Escape(colName)}\"),");
            }

            sb.AppendLine("            },");
            sb.AppendLine($"            StorageName: \"{Escape(storageName)}\",");
            sb.AppendLine($"            Aliases: {FormatAliases(entity)},");

            var primaryKey = scalar.FirstOrDefault(p => GetNamedBool(
                GetAttribute(p, FieldAttribute),
                "IsPrimaryKey"));

            if (primaryKey is not null)
            {
                var pkField = GetAttribute(primaryKey, FieldAttribute);

                var pkId =
                    GetNamedULong(pkField, "ColumnId")
                    ?? columnIds[
                        entity.ToDisplayString() + "." + primaryKey.Name];

                sb.AppendLine(
                    $"            PrimaryKey: new ColumnReference(new EntityId({entityId}), new ColumnId({pkId})),");
            }

            sb.AppendLine("            Fields: new FieldMetadata[]");
            sb.AppendLine("            {");

            foreach (var p in scalar)
            {
                var field = GetAttribute(p, FieldAttribute);

                var fieldName =
                    GetNamedString(field, "Name")
                    ?? p.Name;

                var fieldId =
                    GetNamedULong(field, "Id")
                    ?? fieldIds[
                        entity.ToDisplayString() + "." + p.Name];

                var colName =
                    GetNamedString(field, "StorageName")
                    ?? GetNamedString(field, "Name")
                    ?? p.Name;

                var colId =
                    GetNamedULong(field, "ColumnId")
                    ?? columnIds[
                        entity.ToDisplayString() + "." + p.Name];

                var dimensionAttribute =
                    GetAttribute(p, SemanticDimensionAttribute);

                var dimension =
                    dimensionAttribute is null
                        ? null
                        : GetCtorString(dimensionAttribute, 0);

                var isIndexed =
                    GetNamedBool(field, "Index");

                sb.AppendLine(
                    $"                new FieldMetadata(new FieldId({fieldId}), \"{Escape(fieldName)}\", typeof({p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), new ColumnReference(new EntityId({entityId}), new ColumnId({colId})), Dimension: {ToNullableString(dimension)}, IsIndexed: {(isIndexed ? "true" : "false")}, Aliases: {FormatAliases(p)}),");
            }

            sb.AppendLine("            },");

            var eventAttribute = GetAttribute(entity, EventAttribute);

            if (eventAttribute is not null)
            {
                var occurredAtField = GetCtorString(eventAttribute, 0);

                var occurredAtProperty =
                    occurredAtField is null
                        ? null
                        : scalar.FirstOrDefault(p => p.Name == occurredAtField);

                if (occurredAtProperty is not null)
                {
                    var occurredAtColId =
                        columnIds[
                            entity.ToDisplayString() + "." + occurredAtProperty.Name];

                    sb.AppendLine(
                        $"            IsEvent: true, TemporalColumn: new ColumnReference(new EntityId({entityId}), new ColumnId({occurredAtColId}))));");
                }
                else
                {
                    sb.AppendLine("            IsEvent: true));");
                }
            }
            else
            {
                sb.AppendLine("            IsEvent: false));");
            }
        }

        foreach (var entity in ordered)
        {
            var sourceId = entityIds[entity.ToDisplayString()];

            foreach (var p in entity.GetMembers().OfType<IPropertySymbol>())
            {
                var rel = GetAttribute(p, RelationshipAttribute);

                if (rel is null)
                    continue;

                var target = GetTypeArgument(rel, 0);

                if (target is null)
                    continue;

                var targetId = entityIds[target.ToDisplayString()];
                var relKey = entity.ToDisplayString() + "." + p.Name;

                var id =
                    GetNamedULong(rel, "Id")
                    ?? relationshipIds[relKey];

                var name =
                    GetNamedString(rel, "Name")
                    ?? p.Name;

                var fk =
                    GetCtorString(rel, 1)
                    ?? "Id";

                var pk =
                    GetCtorString(rel, 2)
                    ?? "Id";

                var sourceOwnsForeignKey =
                    entity.GetMembers(fk)
                        .OfType<IPropertySymbol>()
                        .Any();

                var fkOwner =
                    sourceOwnsForeignKey
                        ? entity
                        : target;

                var principalOwner =
                    sourceOwnsForeignKey
                        ? target
                        : entity;

                var fkId = ResolveColumnId(
                    fkOwner,
                    fk,
                    entityIds);

                var principalId = ResolveColumnId(
                    principalOwner,
                    pk,
                    entityIds);

                // SourceKey/TargetKey always describe the key on each side
                // of the semantic relationship, regardless of which side
                // physically owns the foreign key.
                var sourceKeyEntity = sourceId;

                var sourceKeyColumn =
                    sourceOwnsForeignKey
                        ? fkId
                        : principalId;

                var targetKeyEntity = targetId;

                var targetKeyColumn =
                    sourceOwnsForeignKey
                        ? principalId
                        : fkId;

                sb.AppendLine(
                    $"        registry.Register(new RelationshipMetadata(new RelationshipId({id}), new EntityId({sourceId}), new EntityId({targetId}), \"{Escape(name)}\", new ColumnReference(new EntityId({sourceKeyEntity}), new ColumnId({sourceKeyColumn})), new ColumnReference(new EntityId({targetKeyEntity}), new ColumnId({targetKeyColumn})), {IsCollectionExpression(p.Type)}, Aliases: {FormatAliases(p)}));");
            }
        }

        foreach (var conversion in conversions.OrderBy(
                     x => x.ToDisplayString(),
                     StringComparer.Ordinal))
        {
            var attribute = GetAttribute(
                conversion,
                ConversionAttribute);

            var sourceType = GetTypeArgument(attribute, 0);
            var targetType = GetTypeArgument(attribute, 1);

            if (sourceType is null || targetType is null)
                continue;

            var method = conversion.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat);

            sb.AppendLine(
                $"        registry.Register(new ConversionMetadata(typeof({sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), typeof({targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), \"{Escape(method)}\"));");
        }

        foreach (var authorization in authorizations.OrderBy(
                     x => x.ToDisplayString(),
                     StringComparer.Ordinal))
        {
            var attribute = GetAttribute(
                authorization,
                AuthorizationAttribute);

            var explicitId = GetNamedULong(attribute, "Id");
            if (explicitId.HasValue)
                explicitId = GeneratorSemanticIdentity.ValidateExplicitId(explicitId.Value, "authorization");

            var connectionId = GetConstructorULong(attribute, 0);

            if (connectionId is null)
                continue;

            var expression = GetAuthorizationExpression(authorization);

            if (expression is null)
                continue;

            var delegateType =
                GetExpressionDelegateType(authorization.Type);

            if (delegateType is null ||
                delegateType.TypeArguments.Length != 3)
            {
                continue;
            }

            var contextType = delegateType.TypeArguments[0];
            var resourceType = delegateType.TypeArguments[1];
            var returnType = delegateType.TypeArguments[2];

            if (returnType.SpecialType != SpecialType.System_Boolean)
                continue;

            var id =
                explicitId
                ?? GeneratorSemanticIdentity.Hash(
                    GeneratorSemanticIdentity.AuthorizationKey(authorization.ContainingType.ToDisplayString(),
                        authorization.Name));

            var name =
                GetNamedString(attribute, "Name")
                ?? authorization.Name;

            var predicate = BuildAuthorizationPredicate(authorization);

            if (predicate is null)
                continue;

            sb.AppendLine(
                $"        registry.Register(new AuthorizationMetadata(new AuthorizationId({id}), new ConnectionId({connectionId.Value}), \"{Escape(name)}\", \"{Escape(authorization.Name)}\", typeof({contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), typeof({resourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), \"{Escape(expression)}\", {predicate}));");
        }

        foreach (var model in models.OrderBy(
                     x => x.ToDisplayString(),
                     StringComparer.Ordinal))
        {
            var modelId = modelIds[model.ToDisplayString()];

            foreach (var property in model.GetMembers().OfType<IPropertySymbol>())
            {
                var connection = GetAttribute(
                    property,
                    ConnectionAttribute);

                if (connection is null)
                    continue;

                var key =
                    model.ToDisplayString() + "." + property.Name;

                var target = GetTypeArgument(connection, 0);

                if (target is null)
                    connectionMap.TryGetValue(key, out target);

                if (target is null ||
                    !entityIds.TryGetValue(
                        target.ToDisplayString(),
                        out var targetId))
                {
                    continue;
                }

                var connectionId =
                    GetNamedULong(connection, "Id")
                    ?? connectionIds[key];

                var name =
                    GetNamedString(connection, "Name")
                    ?? property.Name;

                var fields = BuildConnectionFields(
                    property,
                    model,
                    target,
                    conversions);

                var fieldText =
                    fields.Count == 0
                        ? "null"
                        : "new ConnectionFieldMetadata[] { " +
                          string.Join(
                              ", ",
                              fields.Select(f =>
                                  $"new ConnectionFieldMetadata(\"{Escape(f.SourceMember)}\", \"{Escape(f.TargetMember)}\", typeof({f.SourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), typeof({f.TargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), {ToNullableString(f.Converter)} )")) +
                          " }";

                sb.AppendLine(
                    $"        registry.Register(new ConnectionMetadata(new ConnectionId({connectionId}), new ModelId({modelId}), new EntityId({targetId}), \"{Escape(name)}\", \"{Escape(property.Name)}\", {fieldText}));");
            }
        }

        sb.AppendLine("        return registry;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        sb.AppendLine();

        EmitSemanticModel(
            sb,
            models,
            modelEntityMap,
            entityIds,
            relationshipIds);

        sb.AppendLine(
            "public sealed class GeneratedMetadataProvider : IMetadataProvider, IMetadataSource");

        sb.AppendLine("{");

        sb.AppendLine(
            "    public IReadOnlyCollection<EntityMetadata> Entities => GeneratedMetadata.Registry.Entities.ToArray();");

        sb.AppendLine(
            "    public IReadOnlyCollection<RelationshipMetadata> Relationships => GeneratedMetadata.Registry.Relationships.ToArray();");

        sb.AppendLine(
            "    public IReadOnlyCollection<ModelMetadata> Models => GeneratedMetadata.Registry.Models.ToArray();");

        sb.AppendLine(
            "    public IReadOnlyCollection<ConnectionMetadata> Connections => GeneratedMetadata.Registry.Connections.ToArray();");

        sb.AppendLine(
            "    public IReadOnlyCollection<ConversionMetadata> Conversions => GeneratedMetadata.Registry.Conversions.ToArray();");

        sb.AppendLine(
            "    public IReadOnlyCollection<AuthorizationMetadata> Authorizations => GeneratedMetadata.Registry.Authorizations.ToArray();");

        sb.AppendLine(
            "    public EntityMetadata GetEntity(EntityId entityId) => GeneratedMetadata.Registry.GetEntity(entityId);");

        sb.AppendLine(
            "    public RelationshipMetadata GetRelationship(RelationshipId relationshipId) => GeneratedMetadata.Registry.GetRelationship(relationshipId);");

        sb.AppendLine(
            "    public ModelMetadata GetModel(ModelId modelId) => GeneratedMetadata.Registry.GetModel(modelId);");

        sb.AppendLine(
            "    public ConnectionMetadata GetConnection(ConnectionId connectionId) => GeneratedMetadata.Registry.GetConnection(connectionId);");

        sb.AppendLine(
            "    public ConversionMetadata? FindConversion(Type sourceType, Type targetType) => GeneratedMetadata.Registry.FindConversion(sourceType, targetType);");

        sb.AppendLine(
            "    public AuthorizationMetadata GetAuthorization(AuthorizationId authorizationId) => GeneratedMetadata.Registry.GetAuthorization(authorizationId);");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitSemanticModel(
        StringBuilder sb,
        ImmutableArray<INamedTypeSymbol> models,
        Dictionary<string, INamedTypeSymbol> modelEntityMap,
        Dictionary<string, ulong> entityIds,
        Dictionary<string, ulong> relationshipIds)
    {
        sb.AppendLine("public static class GeneratedSemanticModel");
        sb.AppendLine("{");
        sb.AppendLine(
            "    /// <summary>Canonical fingerprint of the semantic contract discovered from generated metadata.</summary>");
        sb.AppendLine(
            "    public static string ContractFingerprint => GeneratedMetadata.Registry.Discover().ContractFingerprint;");
        sb.AppendLine();

        foreach (var model in models.OrderBy(
                     x => x.ToDisplayString(),
                     StringComparer.Ordinal))
        {
            if (!modelEntityMap.TryGetValue(
                    model.ToDisplayString(),
                    out var entity) ||
                !entityIds.TryGetValue(
                    entity.ToDisplayString(),
                    out var entityId))
            {
                continue;
            }

            var modelName =
                GetNamedString(
                    GetAttribute(model, ModelAttribute),
                    "Name")
                ?? model.Name;

            var modelProperties = model.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p =>
                    p.DeclaredAccessibility == Accessibility.Public &&
                    !p.IsStatic)
                .ToArray();

            var entityProperties = entity.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p =>
                    p.DeclaredAccessibility == Accessibility.Public &&
                    !p.IsStatic)
                .ToDictionary(
                    p => p.Name,
                    StringComparer.Ordinal);

            sb.AppendLine(
                $"    public static class {model.Name}");

            sb.AppendLine("    {");

            // Do not call this member "Name": Name may itself be a semantic field.
            sb.AppendLine(
                $"        public const string ModelName = \"{Escape(modelName)}\";");

            sb.AppendLine(
                $"        public static readonly EntityId Entity = new({entityId});");

            var fieldIds = AllocateFieldIds(entity);

            var fieldMembers =
                new List<(string Identifier, ulong FieldId, string SemanticName)>();

            var usedIdentifiers =
                new HashSet<string>(StringComparer.Ordinal)
                {
                    model.Name,
                    "ModelName",
                    "Entity",
                    "All"
                };

            foreach (var property in modelProperties)
            {
                if (!entityProperties.TryGetValue(
                        property.Name,
                        out var entityProperty))
                {
                    continue;
                }

                var field =
                    GetAttribute(
                        entityProperty,
                        FieldAttribute);

                var fieldId =
                    fieldIds.TryGetValue(
                        entityProperty.ToDisplayString(),
                        out var generatedFieldId)
                        ? generatedFieldId
                        : 0UL;

                if (fieldId == 0)
                    continue;

                var identifier =
                    GetGeneratedSemanticFieldIdentifier(
                        property.Name,
                        usedIdentifiers);

                usedIdentifiers.Add(identifier);

                fieldMembers.Add(
                    (
                        identifier,
                        fieldId,
                        property.Name));

                sb.AppendLine(
                    $"        public static readonly GeneratedSemanticField {identifier} = " +
                    $"new(Entity, new FieldId({fieldId}), \"{Escape(property.Name)}\");");
            }

            sb.AppendLine(
                "        public static IReadOnlyList<FieldId> All { get; } = new FieldId[]");

            sb.AppendLine("        {");

            foreach (var field in fieldMembers)
                sb.AppendLine($"            {field.Identifier}.Id,");

            sb.AppendLine("        };");

            // Relationships are declared on the storage (ERP) entity, not on
            // the application model, but callers should never need to walk
            // the registry by name to find a RelationshipId: emit one
            // strongly-typed, named constant per [FoundgineRelationship]
            // property found on the mapped entity. This removes the need for
            // any hand-written MetadataRegistry.Relationships.Single(...)
            // lookup helper in application code.
            var relationshipProperties = entity.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p =>
                    p.DeclaredAccessibility == Accessibility.Public &&
                    !p.IsStatic &&
                    GetAttribute(p, RelationshipAttribute) is not null)
                .ToArray();

            if (relationshipProperties.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("        public static class Relationships");
                sb.AppendLine("        {");

                var usedRelationshipIdentifiers =
                    new HashSet<string>(StringComparer.Ordinal);

                foreach (var property in relationshipProperties)
                {
                    var relationshipKey =
                        entity.ToDisplayString() + "." + property.Name;

                    if (!relationshipIds.TryGetValue(
                            relationshipKey,
                            out var relationshipId))
                    {
                        continue;
                    }

                    var attribute =
                        GetAttribute(property, RelationshipAttribute);

                    var relationshipName =
                        GetNamedString(attribute, "Name")
                        ?? property.Name;

                    var identifier =
                        GetGeneratedSemanticFieldIdentifier(
                            relationshipName,
                            usedRelationshipIdentifiers);

                    usedRelationshipIdentifiers.Add(identifier);

                    sb.AppendLine(
                        $"            public static readonly RelationshipId {identifier} = new({relationshipId});");
                }

                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string GetGeneratedSemanticFieldIdentifier(
        string propertyName,
        HashSet<string> usedIdentifiers)
    {
        var candidate = propertyName;

        if (!usedIdentifiers.Contains(candidate))
            return candidate;

        candidate = propertyName + "Field";

        if (!usedIdentifiers.Contains(candidate))
            return candidate;

        var suffix = 2;

        while (usedIdentifiers.Contains(candidate + suffix))
            suffix++;

        return candidate + suffix;
    }

    private static Dictionary<string, INamedTypeSymbol> BuildModelEntityMap(
        ImmutableArray<INamedTypeSymbol> declarations)
    {
        var result =
            new Dictionary<string, INamedTypeSymbol>(
                StringComparer.Ordinal);

        foreach (var declaration in declarations.OrderBy(
                     x => x.ToDisplayString(),
                     StringComparer.Ordinal))
        {
            foreach (var attribute in declaration.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() ==
                                                                             ModelEntityMapAttribute))
            {
                var model = GetTypeArgument(attribute, 0);
                var entity = GetTypeArgument(attribute, 1);

                if (model is not null && entity is not null)
                    result[model.ToDisplayString()] = entity;
            }
        }

        return result;
    }

    private static Dictionary<string, INamedTypeSymbol> BuildConnectionMap(
        ImmutableArray<INamedTypeSymbol> declarations)
    {
        var result =
            new Dictionary<string, INamedTypeSymbol>(
                StringComparer.Ordinal);

        foreach (var declaration in declarations.OrderBy(
                     x => x.ToDisplayString(),
                     StringComparer.Ordinal))
        {
            foreach (var attribute in declaration.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() ==
                                                                             ConnectionMapAttribute))
            {
                var model = GetTypeArgument(attribute, 0);
                var member = GetCtorString(attribute, 1);
                var entity = GetTypeArgument(attribute, 2);

                if (model is not null &&
                    entity is not null &&
                    !string.IsNullOrWhiteSpace(member))
                {
                    result[
                        model.ToDisplayString() + "." + member] = entity;
                }
            }
        }

        return result;
    }

    private sealed class ConnectionField
    {
        public ConnectionField(
            string sourceMember,
            string targetMember,
            ITypeSymbol sourceType,
            ITypeSymbol targetType,
            string? converter)
        {
            SourceMember = sourceMember;
            TargetMember = targetMember;
            SourceType = sourceType;
            TargetType = targetType;
            Converter = converter;
        }

        public string SourceMember { get; }

        public string TargetMember { get; }

        public ITypeSymbol SourceType { get; }

        public ITypeSymbol TargetType { get; }

        public string? Converter { get; }
    }

    private static List<ConnectionField> BuildConnectionFields(
        IPropertySymbol connectionProperty,
        INamedTypeSymbol model,
        INamedTypeSymbol target,
        ImmutableArray<IMethodSymbol> conversions)
    {
        var expressionFields =
            BuildExpressionConnectionFields(
                connectionProperty,
                model,
                target,
                conversions);

        if (expressionFields is not null)
            return expressionFields;

        return BuildConventionConnectionFields(
            model,
            target,
            conversions);
    }

    private static List<ConnectionField>? BuildExpressionConnectionFields(
        IPropertySymbol connectionProperty,
        INamedTypeSymbol model,
        INamedTypeSymbol target,
        ImmutableArray<IMethodSymbol> conversions)
    {
        var syntax =
            connectionProperty
                .DeclaringSyntaxReferences
                .FirstOrDefault()
                ?.GetSyntax() as PropertyDeclarationSyntax;

        if (syntax?.ExpressionBody?.Expression
            is not LambdaExpressionSyntax lambda)
        {
            return null;
        }

        if (lambda.Body
            is not AnonymousObjectCreationExpressionSyntax anonymous)
        {
            return null;
        }

        var sourceParameter = lambda switch
        {
            SimpleLambdaExpressionSyntax simple =>
                simple.Parameter.Identifier.Text,

            ParenthesizedLambdaExpressionSyntax parenthesized =>
                parenthesized.ParameterList.Parameters
                    .FirstOrDefault()
                    ?.Identifier.Text,

            _ => null
        };

        if (string.IsNullOrWhiteSpace(sourceParameter))
            return null;

        var result = new List<ConnectionField>();

        foreach (var initializer in anonymous.Initializers)
        {
            var targetMember =
                initializer.NameEquals?.Name.Identifier.Text;

            var expression =
                initializer.Expression;

            if (string.IsNullOrWhiteSpace(targetMember))
            {
                targetMember = expression switch
                {
                    MemberAccessExpressionSyntax member =>
                        member.Name.Identifier.Text,

                    IdentifierNameSyntax identifier =>
                        identifier.Identifier.Text,

                    _ => null
                };
            }

            if (string.IsNullOrWhiteSpace(targetMember))
                continue;

            var targetMemberName = targetMember!;

            var targetProperty =
                target.GetMembers(targetMemberName)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault();

            if (targetProperty is null)
                continue;

            var sourceMember =
                sourceParameter is null
                    ? null
                    : GetDirectSourceMember(
                        expression,
                        sourceParameter);

            if (sourceMember is null)
                continue;

            var sourceProperty =
                model.GetMembers(sourceMember)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault();

            if (sourceProperty is null)
                continue;

            IMethodSymbol? converter = null;

            if (!SymbolEqualityComparer.Default.Equals(
                    sourceProperty.Type,
                    targetProperty.Type))
            {
                converter =
                    FindConversionForExpression(
                        expression,
                        sourceProperty.Type,
                        targetProperty.Type,
                        conversions);

                if (converter is null)
                    continue;
            }

            result.Add(
                new ConnectionField(
                    sourceMember,
                    targetMemberName,
                    sourceProperty.Type,
                    targetProperty.Type,
                    converter?.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        return result;
    }

    private static string? GetDirectSourceMember(
        ExpressionSyntax expression,
        string sourceParameter)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.ArgumentList.Arguments.Count == 1)
        {
            return GetDirectSourceMember(
                invocation.ArgumentList.Arguments[0].Expression,
                sourceParameter);
        }

        if (expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.Text;

        if (expression is not MemberAccessExpressionSyntax member)
            return null;

        if (member.Expression is IdentifierNameSyntax receiver &&
            receiver.Identifier.Text == sourceParameter)
        {
            return member.Name.Identifier.Text;
        }

        return null;
    }

    private static IMethodSymbol? FindConversionForExpression(
        ExpressionSyntax expression,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ImmutableArray<IMethodSymbol> conversions)
    {
        var methodName =
            expression is InvocationExpressionSyntax invocation
                ? GetInvokedMethodName(invocation.Expression)
                : null;

        return conversions.FirstOrDefault(method =>
        {
            if (methodName is not null &&
                method.Name != methodName)
            {
                return false;
            }

            var attribute =
                GetAttribute(
                    method,
                    ConversionAttribute);

            var from =
                GetTypeArgument(attribute, 0);

            var to =
                GetTypeArgument(attribute, 1);

            return from is not null &&
                   to is not null &&
                   SymbolEqualityComparer.Default.Equals(
                       from,
                       sourceType) &&
                   SymbolEqualityComparer.Default.Equals(
                       to,
                       targetType);
        });
    }

    private static string? GetInvokedMethodName(
        ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier =>
                identifier.Identifier.Text,

            MemberAccessExpressionSyntax member =>
                member.Name.Identifier.Text,

            _ => null
        };

    private static List<ConnectionField> BuildConventionConnectionFields(
        INamedTypeSymbol model,
        INamedTypeSymbol target,
        ImmutableArray<IMethodSymbol> conversions)
    {
        var sourceProperties = model.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                !p.IsStatic)
            .ToArray();

        var targetProperties = target.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                !p.IsStatic)
            .Where(p =>
                GetAttribute(
                    p,
                    RelationshipAttribute) is null)
            .ToArray();

        var result = new List<ConnectionField>();

        foreach (var destination in targetProperties)
        {
            var source =
                sourceProperties.FirstOrDefault(p => p.Name == destination.Name);

            if (source is not null &&
                SymbolEqualityComparer.Default.Equals(
                    source.Type,
                    destination.Type))
            {
                result.Add(
                    new ConnectionField(
                        source.Name,
                        destination.Name,
                        source.Type,
                        destination.Type,
                        null));

                continue;
            }

            var candidates = sourceProperties
                .Select(sourceProperty => new
                {
                    Property = sourceProperty,
                    Conversion = conversions.FirstOrDefault(method =>
                    {
                        var attribute =
                            GetAttribute(
                                method,
                                ConversionAttribute);

                        var from =
                            GetTypeArgument(attribute, 0);

                        var to =
                            GetTypeArgument(attribute, 1);

                        return from is not null &&
                               to is not null &&
                               SymbolEqualityComparer.Default.Equals(
                                   from,
                                   sourceProperty.Type) &&
                               SymbolEqualityComparer.Default.Equals(
                                   to,
                                   destination.Type);
                    })
                })
                .Where(x => x.Conversion is not null)
                .ToArray();

            if (candidates.Length == 1)
            {
                var candidate = candidates[0];

                result.Add(
                    new ConnectionField(
                        candidate.Property.Name,
                        destination.Name,
                        candidate.Property.Type,
                        destination.Type,
                        candidate.Conversion!.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat)));
            }
        }

        return result;
    }

    private static INamedTypeSymbol? GetExpressionDelegateType(
        ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named ||
            named.Name != "Expression" ||
            named.TypeArguments.Length != 1)
        {
            return null;
        }

        return named.TypeArguments[0] as INamedTypeSymbol;
    }

    private static string? BuildAuthorizationPredicate(
        IPropertySymbol property)
    {
        var syntax =
            property
                .DeclaringSyntaxReferences
                .FirstOrDefault()
                ?.GetSyntax() as PropertyDeclarationSyntax;

        if (syntax?.ExpressionBody?.Expression
            is not LambdaExpressionSyntax lambda)
        {
            return null;
        }

        var parameters = lambda switch
        {
            SimpleLambdaExpressionSyntax simple =>
                new[] { simple.Parameter.Identifier.Text },

            ParenthesizedLambdaExpressionSyntax parenthesized =>
                parenthesized.ParameterList.Parameters
                    .Select(p => p.Identifier.Text)
                    .ToArray(),

            _ => Array.Empty<string>()
        };

        if (parameters.Length == 0)
            return null;

        var body = lambda switch
        {
            SimpleLambdaExpressionSyntax simple =>
                simple.Body,

            ParenthesizedLambdaExpressionSyntax parenthesized =>
                parenthesized.Body,

            _ => null
        };

        return body is ExpressionSyntax expression
            ? BuildPredicateNode(expression, parameters)
            : null;
    }

    private static string? BuildPredicateNode(
        ExpressionSyntax expression,
        IReadOnlyList<string> parameters)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return BuildPredicateNode(
                    parenthesized.Expression,
                    parameters);

            case IdentifierNameSyntax identifier
                when parameters.Count > 0 &&
                     identifier.Identifier.Text == parameters[0]:

                return
                    $"Foundgine.Core.Abstractions.AuthorizationPredicate.ContextParameter(\"{Escape(identifier.Identifier.Text)}\")";

            case IdentifierNameSyntax identifier
                when parameters.Count > 1 &&
                     identifier.Identifier.Text == parameters[1]:

                return
                    $"Foundgine.Core.Abstractions.AuthorizationPredicate.ResourceParameter(\"{Escape(identifier.Identifier.Text)}\")";

            case MemberAccessExpressionSyntax member:
            {
                var target =
                    BuildPredicateNode(
                        member.Expression,
                        parameters);

                return target is null
                    ? null
                    : $"Foundgine.Core.Abstractions.AuthorizationPredicate.Member({target}, \"{Escape(member.Name.Identifier.Text)}\")";
            }

            case LiteralExpressionSyntax literal:
                return
                    $"Foundgine.Core.Abstractions.AuthorizationPredicate.Constant(\"{Escape(literal.ToString())}\")";

            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.EqualsExpression):

                return BuildBinaryPredicate(
                    binary,
                    "Equal",
                    parameters);

            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.NotEqualsExpression):

                return BuildBinaryPredicate(
                    binary,
                    "NotEqual",
                    parameters);

            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.LogicalAndExpression):

                return BuildBinaryPredicate(
                    binary,
                    "And",
                    parameters);

            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.LogicalOrExpression):

                return BuildBinaryPredicate(
                    binary,
                    "Or",
                    parameters);

            case PrefixUnaryExpressionSyntax unary
                when unary.IsKind(SyntaxKind.LogicalNotExpression):
            {
                var operand =
                    BuildPredicateNode(
                        unary.Operand,
                        parameters);

                return operand is null
                    ? null
                    : $"Foundgine.Core.Abstractions.AuthorizationPredicate.Not({operand})";
            }

            default:
                return null;
        }
    }

    private static string? BuildBinaryPredicate(
        BinaryExpressionSyntax binary,
        string operation,
        IReadOnlyList<string> parameters)
    {
        var left =
            BuildPredicateNode(
                binary.Left,
                parameters);

        var right =
            BuildPredicateNode(
                binary.Right,
                parameters);

        return left is null || right is null
            ? null
            : $"Foundgine.Core.Abstractions.AuthorizationPredicate.{operation}({left}, {right})";
    }

    private static string? GetAuthorizationExpression(
        IPropertySymbol property)
    {
        var syntax =
            property
                .DeclaringSyntaxReferences
                .FirstOrDefault()
                ?.GetSyntax() as PropertyDeclarationSyntax;

        return syntax?.ExpressionBody?.Expression
            is LambdaExpressionSyntax lambda
            ? lambda.ToString()
            : null;
    }

    private static ulong? GetConstructorULong(
        AttributeData? attribute,
        int index)
    {
        if (attribute is null ||
            index < 0 ||
            index >= attribute.ConstructorArguments.Length)
        {
            return null;
        }

        var value = attribute.ConstructorArguments[index].Value;

        return value switch
        {
            ulong ulongValue when ulongValue != 0 => ulongValue,
            uint uintValue when uintValue != 0 => uintValue,
            ushort ushortValue when ushortValue != 0 => ushortValue,
            int intValue when intValue > 0 => (ulong)intValue,
            long longValue when longValue > 0 => (ulong)longValue,
            _ => null
        };
    }

    private static string ToNullableString(string? value) =>
        value is null
            ? "null"
            : $"\"{Escape(value)}\"";

    private static Dictionary<string, ulong> AllocateModelIds(
        IReadOnlyList<INamedTypeSymbol> models)
    {
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var used = new HashSet<ulong>();

        foreach (var model in models.OrderBy(x => x.ToDisplayString(), StringComparer.Ordinal))
        {
            var attribute = GetAttribute(model, ModelAttribute);
            var explicitId = GetNamedULong(attribute, "Id");
            if (explicitId.HasValue)
            {
                explicitId = GeneratorSemanticIdentity.ValidateExplicitId(explicitId.Value, "model");
                if (!used.Add(explicitId.Value))
                    throw new InvalidOperationException($"Duplicate Foundgine model ID {explicitId.Value}.");
                result[model.ToDisplayString()] = explicitId.Value;
            }
        }

        foreach (var model in models.OrderBy(x => x.ToDisplayString(), StringComparer.Ordinal))
        {
            var clrKey = model.ToDisplayString();
            if (result.ContainsKey(clrKey)) continue;
            var semanticName = GetNamedString(GetAttribute(model, ModelAttribute), "Name") ?? model.Name;
            var candidate = GeneratorSemanticIdentity.Hash(GeneratorSemanticIdentity.ModelKey(semanticName));
            if (!used.Add(candidate))
                throw new InvalidOperationException($"Model identity collision for '{semanticName}' (ID {candidate}).");
            result[clrKey] = candidate;
        }

        return result;
    }

    private static Dictionary<string, ulong> AllocateConnectionIds(
        IReadOnlyList<INamedTypeSymbol> models)
    {
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var used = new HashSet<ulong>();

        var declarations = models.SelectMany(model => model.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => GetAttribute(p, ConnectionAttribute) is not null)
                .Select(p =>
                {
                    var attribute = GetAttribute(p, ConnectionAttribute);
                    var name = GetNamedString(attribute, "Name") ?? p.Name;
                    return (Key: model.ToDisplayString() + "." + p.Name,
                        ModelName: GetNamedString(GetAttribute(model, ModelAttribute), "Name") ?? model.Name,
                        ConnectionName: name,
                        Attribute: attribute);
                }))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (var (key, _, _, attribute) in declarations)
        {
            var explicitId = GetNamedULong(attribute, "Id");
            if (explicitId.HasValue)
            {
                explicitId = GeneratorSemanticIdentity.ValidateExplicitId(explicitId.Value, "connection");
                if (!used.Add(explicitId.Value))
                    throw new InvalidOperationException($"Duplicate Foundgine connection ID {explicitId.Value}.");
                result[key] = explicitId.Value;
            }
        }

        foreach (var (key, modelName, connectionName, _) in declarations)
        {
            if (result.ContainsKey(key)) continue;
            var candidate =
                GeneratorSemanticIdentity.Hash(GeneratorSemanticIdentity.ConnectionKey(modelName, connectionName));
            if (!used.Add(candidate))
                throw new InvalidOperationException(
                    $"Connection identity collision for '{modelName}.{connectionName}' (ID {candidate}).");
            result[key] = candidate;
        }

        return result;
    }

    /// <summary>
    /// Assigns relationship identities from a stable hash of the semantic
    /// entity and relationship name ("Entity.Relationship"), independent of
    /// declaration order and CLR metadata layout.
    /// This is what lets relationship authors omit <c>[FoundgineRelationship(..., Id = ...)]</c> entirely:
    /// the identity is derived from the declaration itself, so it survives
    /// reordering, module merges and new relationships being added elsewhere
    /// without renumbering anything. Explicit ids are honored first (and
    /// reserved against collision) so existing manually-numbered
    /// relationships keep their identity; every other relationship is then
    /// hash-allocated. Hash collisions are treated as generator errors rather
    /// than resolved by probing, because probing would make identity depend on
    /// the set and ordering of unrelated declarations.
    /// </summary>
    private static Dictionary<string, ulong> AllocateRelationshipIds(
        IReadOnlyList<INamedTypeSymbol> entities)
    {
        var declarations = entities
            .SelectMany(entity => entity.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => GetAttribute(p, RelationshipAttribute) is not null)
                .Select(p =>
                {
                    var attribute = GetAttribute(p, RelationshipAttribute);
                    var relationshipName =
                        GetNamedString(attribute, "Name")
                        ?? p.Name;
                    var semanticKey =
                        GetEntityName(entity) + "." + relationshipName;

                    return (
                        Key: entity.ToDisplayString() + "." + p.Name,
                        SemanticKey: semanticKey,
                        Attribute: attribute);
                }))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();

        var result =
            new Dictionary<string, ulong>(
                StringComparer.Ordinal);

        var used = new HashSet<ulong>();

        foreach (var (key, _, attribute) in declarations)
        {
            var explicitId =
                GetNamedULong(attribute, "Id");
            if (explicitId.HasValue)
                explicitId = GeneratorSemanticIdentity.ValidateExplicitId(explicitId.Value, "relationship");

            if (explicitId.HasValue)
            {
                if (!used.Add(explicitId.Value))
                {
                    throw new InvalidOperationException(
                        $"Duplicate Foundgine relationship ID {explicitId.Value}.");
                }

                result[key] = explicitId.Value;
            }
        }

        foreach (var (key, semanticKey, _) in declarations)
        {
            if (result.ContainsKey(key))
                continue;

            var candidate =
                GeneratorSemanticIdentity.Hash(GeneratorSemanticIdentity.RelationshipNamespace + ":" + semanticKey);

            if (candidate == 0 || !used.Add(candidate))
            {
                throw new InvalidOperationException(
                    $"Relationship identity collision for '{key}' (ID {candidate}).");
            }

            result[key] = candidate;
        }

        return result;
    }

    /// <summary>
    /// Assigns stable semantic entity identities from the canonical entity name.
    /// Identity is independent of declaration order and therefore survives
    /// adding, removing, or reordering unrelated entities. Explicit legacy ids
    /// remain supported for compatibility. Hash collisions are generator errors
    /// rather than resolved by probing because probing would make identity depend
    /// on unrelated entities.
    /// </summary>
    private static Dictionary<string, ulong> AllocateEntityIds(
        IReadOnlyList<INamedTypeSymbol> entities)
    {
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var used = new HashSet<ulong>();
        var semanticNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in entities.OrderBy(
                     x => x.ToDisplayString(), StringComparer.Ordinal))
        {
            var semanticName = GetEntityName(entity);
            if (!semanticNames.Add(semanticName))
                throw new InvalidOperationException(
                    $"Duplicate Foundgine semantic entity name '{semanticName}'. Module composition requires unique semantic entity names.");

            var explicitId =
                GetNamedULong(
                    GetAttribute(entity, EntityAttribute),
                    "Id");
            if (explicitId.HasValue)
                explicitId = GeneratorSemanticIdentity.ValidateExplicitId(explicitId.Value, "entity");

            if (explicitId.HasValue)
            {
                if (!used.Add(explicitId.Value))
                    throw new InvalidOperationException(
                        $"Duplicate Foundgine entity ID {explicitId.Value}.");

                result[entity.ToDisplayString()] = explicitId.Value;
            }
        }

        foreach (var entity in entities.OrderBy(
                     x => x.ToDisplayString(), StringComparer.Ordinal))
        {
            var clrKey = entity.ToDisplayString();
            if (result.ContainsKey(clrKey))
                continue;

            var semanticName = GetEntityName(entity);
            var candidate = GeneratorSemanticIdentity.Hash(GeneratorSemanticIdentity.EntityKey(semanticName));

            if (candidate == 0 || !used.Add(candidate))
                throw new InvalidOperationException(
                    $"Entity identity collision for '{semanticName}' (ID {candidate}).");

            result[clrKey] = candidate;
        }

        return result;
    }

    /// <summary>
    /// Assigns stable semantic field identities from the entity semantic name
    /// and field semantic name. Field identity is independent of declaration
    /// order and therefore survives adding, removing, or reordering unrelated
    /// fields. Explicit legacy ids remain supported for compatibility.
    /// Hash collisions are treated as generator errors rather than resolved by
    /// probing, because probing would make identity depend on unrelated fields.
    /// Column identity is deliberately allocated separately: a semantic field
    /// id must never implicitly become a physical column id.
    /// </summary>
    private static Dictionary<string, ulong> AllocateFieldIds(
        INamedTypeSymbol entity)
    {
        var scalar = entity.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                !p.IsStatic)
            .Where(p =>
                p.GetAttributes().All(a => a.AttributeClass?.ToDisplayString() != RelationshipAttribute))
            .ToArray();

        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var used = new HashSet<ulong>();

        foreach (var property in scalar)
        {
            var attribute = GetAttribute(property, FieldAttribute);
            var explicitId = GetNamedULong(attribute, "Id");
            if (explicitId.HasValue)
                explicitId = GeneratorSemanticIdentity.ValidateExplicitId(explicitId.Value, "field");
            if (!explicitId.HasValue)
                continue;

            if (!used.Add(explicitId.Value))
                throw new InvalidOperationException(
                    $"Duplicate Foundgine field ID {explicitId.Value} on '{entity.ToDisplayString()}'.");

            result[entity.ToDisplayString() + "." + property.Name] = explicitId.Value;
        }

        foreach (var property in scalar.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var key = entity.ToDisplayString() + "." + property.Name;
            if (result.ContainsKey(key))
                continue;

            var attribute = GetAttribute(property, FieldAttribute);
            var fieldName = GetNamedString(attribute, "Name") ?? property.Name;
            var semanticKey = GetEntityName(entity) + "." + fieldName;
            var candidate =
                GeneratorSemanticIdentity.Hash(GeneratorSemanticIdentity.FieldNamespace + ":" + semanticKey);

            if (candidate == 0 || !used.Add(candidate))
                throw new InvalidOperationException(
                    $"Field identity collision for '{key}' (ID {candidate}).");

            result[key] = candidate;
        }

        return result;
    }

    /// <summary>Allocates physical column identities independently from semantic FieldId values.</summary>
    private static Dictionary<string, ulong> AllocateColumnIds(
        INamedTypeSymbol entity,
        string storageName)
    {
        var scalar = entity.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                !p.IsStatic)
            .Where(p => p.GetAttributes().All(a => a.AttributeClass?.ToDisplayString() != RelationshipAttribute))
            .ToArray();

        var used = new HashSet<ulong>();
        foreach (var property in scalar)
        {
            var explicitId = GetNamedULong(GetAttribute(property, FieldAttribute), "ColumnId");
            if (explicitId.HasValue)
                explicitId = GeneratorSemanticIdentity.ValidateExplicitId(explicitId.Value, "column");
            if (explicitId.HasValue)
                used.Add(explicitId.Value);
        }

        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var property in scalar)
        {
            var key = entity.ToDisplayString() + "." + property.Name;
            var explicitId = GetNamedULong(GetAttribute(property, FieldAttribute), "ColumnId");
            if (explicitId.HasValue)
            {
                result[key] = explicitId.Value;
                continue;
            }

            var field = GetAttribute(property, FieldAttribute);
            var columnName =
                GetNamedString(field, "StorageName")
                ?? GetNamedString(field, "Name")
                ?? property.Name;
            var canonicalKey = "column:" + storageName + "." + columnName;
            var candidate = GeneratorSemanticIdentity.Hash(canonicalKey);

            if (candidate == 0 || !used.Add(candidate))
                throw new InvalidOperationException(
                    $"Column identity collision for '{canonicalKey}' (ID {candidate}).");

            result[key] = candidate;
        }

        return result;
    }

    private static AttributeData? GetAttribute(
        ISymbol symbol,
        string fullName) =>
        symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() ==
                                 fullName);

    private static string FormatAliases(ISymbol symbol)
    {
        // Each attribute instance's declared names all share that instance's
        // Weight (or no weight, when unset). Stacking the attribute per name
        // is how callers give different names different weights.
        var entries = symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "Foundgine.Providers.Aot.FoundgineAliasAttribute")
            .SelectMany(a =>
            {
                var names = a.ConstructorArguments.Length == 1
                    ? ExtractAliasNames(a.ConstructorArguments[0])
                    : [];
                var weight = GetNamedInt(a, "Weight");
                return names.Select(name => (Name: name, Weight: weight));
            })
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();

        if (entries.Length == 0)
            return "null";

        return "new AliasDeclaration[] { "
               + string.Join(", ", entries.Select(a =>
                   $"new AliasDeclaration(\"{Escape(a.Name)}\", {(a.Weight is int w ? w.ToString() : "null")})"))
               + " }";
    }

    /// <summary>
    /// The alias attribute's <c>params string[] names</c> parameter is always
    /// represented by Roslyn as a single <see cref="TypedConstantKind.Array"/>
    /// constructor argument, whether the caller wrote a single name, several
    /// positional names, or an array/collection-expression literal. This
    /// flattens that argument back into the individual alias names.
    /// </summary>
    private static IEnumerable<string> ExtractAliasNames(TypedConstant argument)
    {
        if (argument.Kind == TypedConstantKind.Array)
        {
            foreach (var element in argument.Values)
            {
                if (element.Value is string name && !string.IsNullOrWhiteSpace(name))
                    yield return name;
            }
        }
        else if (argument.Value is string single && !string.IsNullOrWhiteSpace(single))
        {
            yield return single;
        }
    }

    private static string GetEntityName(
        INamedTypeSymbol type) =>
        GetNamedString(
            GetAttribute(type, EntityAttribute),
            "Name")
        ?? type.Name;

    private static string? GetEntityStorageName(
        INamedTypeSymbol type) =>
        GetNamedString(
            GetAttribute(type, EntityAttribute),
            "StorageName");

    private static string? GetNamedString(
        AttributeData? attribute,
        string name) =>
        attribute?
            .NamedArguments
            .FirstOrDefault(x => x.Key == name)
            .Value
            .Value as string;

    private static ulong? GetNamedULong(
        AttributeData? attribute,
        string name)
    {
        if (attribute is null)
            return null;

        var argument = attribute.NamedArguments
            .FirstOrDefault(x => x.Key == name);

        if (argument.Key is null)
            return null;

        var value = argument.Value.Value;

        return value switch
        {
            ulong ulongValue => ulongValue,
            uint uintValue => uintValue,
            ushort ushortValue => ushortValue,
            byte byteValue => byteValue,
            int intValue when intValue >= 0 => (ulong)intValue,
            long longValue when longValue >= 0 => (ulong)longValue,
            _ => null
        };
    }

    private static bool GetNamedBool(
        AttributeData? attribute,
        string name) =>
        attribute?
            .NamedArguments
            .FirstOrDefault(x => x.Key == name)
            .Value
            .Value is true;

    private static int? GetNamedInt(
        AttributeData? attribute,
        string name)
    {
        if (attribute is null)
            return null;

        var argument = attribute.NamedArguments
            .FirstOrDefault(x => x.Key == name);

        if (argument.Key is null)
            return null;

        return argument.Value.Value switch
        {
            int intValue => intValue,
            uint uintValue => (int)uintValue,
            byte byteValue => byteValue,
            ushort ushortValue => ushortValue,
            long longValue => (int)longValue,
            ulong ulongValue => (int)ulongValue,
            _ => null
        };
    }

    private static string? GetCtorString(
        AttributeData attribute,
        int index) =>
        index < attribute.ConstructorArguments.Length
            ? attribute.ConstructorArguments[index].Value as string
            : null;

    private static INamedTypeSymbol? GetTypeArgument(
        AttributeData? attribute,
        int index) =>
        attribute is not null &&
        index < attribute.ConstructorArguments.Length
            ? attribute.ConstructorArguments[index].Value
                as INamedTypeSymbol
            : null;

    private static ulong ResolveColumnId(
        INamedTypeSymbol entity,
        string propertyName,
        Dictionary<string, ulong> _)
    {
        var property =
            entity.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault();

        if (property is null)
            return 0;

        var field =
            GetAttribute(
                property,
                FieldAttribute);

        var explicitId =
            GetNamedULong(field, "ColumnId");

        if (explicitId.HasValue)
            return explicitId.Value;

        var key =
            entity.ToDisplayString() +
            "." +
            property.Name;

        // Physical column identity is intentionally independent from the
        // semantic FieldId. ColumnId is derived from the physical storage
        // identity (table/storage name + column name), not declaration order.
        var storageName = GetEntityStorageName(entity) ?? GetEntityName(entity);
        return AllocateColumnIds(entity, storageName)[key];
    }

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");

    private static bool ValidateRelationships(
        SourceProductionContext spc,
        IReadOnlyList<INamedTypeSymbol> entities)
    {
        var entitySet =
            new HashSet<string>(
                entities.Select(x => x.ToDisplayString()),
                StringComparer.Ordinal);

        var valid = true;

        foreach (var entity in entities)
        {
            foreach (var navigation in entity
                         .GetMembers()
                         .OfType<IPropertySymbol>())
            {
                var relationship =
                    GetAttribute(
                        navigation,
                        RelationshipAttribute);

                if (relationship is null)
                    continue;

                var location = GetLocation(navigation);

                var target =
                    GetTypeArgument(
                        relationship,
                        0);

                if (target is null ||
                    !entitySet.Contains(
                        target.ToDisplayString()))
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            RelationshipTargetMustBeEntity,
                            location,
                            entity.Name,
                            navigation.Name,
                            target?.ToDisplayString()
                            ?? "<missing>"));

                    valid = false;
                    continue;
                }

                var navigationTarget =
                    GetNavigationTargetType(
                        navigation.Type);

                if (navigationTarget is null ||
                    !SymbolEqualityComparer.Default.Equals(
                        navigationTarget,
                        target))
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            RelationshipNavigationTargetMismatch,
                            location,
                            entity.Name,
                            navigation.Name,
                            target.ToDisplayString(),
                            navigationTarget?.ToDisplayString()
                            ?? navigation.Type.ToDisplayString()));

                    valid = false;
                }

                var foreignKeyName =
                    GetCtorString(
                        relationship,
                        1)
                    ?? "Id";

                var principalKeyName =
                    GetCtorString(
                        relationship,
                        2)
                    ?? "Id";

                var sourceForeignKey =
                    entity.GetMembers(foreignKeyName)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault();

                var targetForeignKey =
                    target.GetMembers(foreignKeyName)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault();

                if (sourceForeignKey is not null &&
                    targetForeignKey is not null)
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            RelationshipForeignKeyAmbiguous,
                            location,
                            entity.Name,
                            navigation.Name,
                            foreignKeyName,
                            entity.Name,
                            target.Name));

                    valid = false;
                    continue;
                }

                var sourceOwnsForeignKey =
                    sourceForeignKey is not null;

                var foreignKey =
                    sourceForeignKey ??
                    targetForeignKey;

                if (foreignKey is null)
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            RelationshipForeignKeyMissing,
                            location,
                            entity.Name,
                            navigation.Name,
                            foreignKeyName));

                    valid = false;
                    continue;
                }

                if (GetAttribute(
                        foreignKey,
                        RelationshipAttribute) is not null)
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            RelationshipKeyMustBeScalar,
                            location,
                            entity.Name,
                            navigation.Name,
                            foreignKeyName));

                    valid = false;
                    continue;
                }

                var principalEntity =
                    sourceOwnsForeignKey
                        ? target
                        : entity;

                var principalKey =
                    principalEntity
                        .GetMembers(principalKeyName)
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault();

                if (principalKey is null)
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            RelationshipPrincipalKeyMissing,
                            location,
                            entity.Name,
                            navigation.Name,
                            principalKeyName,
                            principalEntity.Name));

                    valid = false;
                    continue;
                }

                if (GetAttribute(
                        principalKey,
                        RelationshipAttribute) is not null)
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            RelationshipKeyMustBeScalar,
                            location,
                            entity.Name,
                            navigation.Name,
                            principalKeyName));

                    valid = false;
                    continue;
                }

                if (!SymbolEqualityComparer.IncludeNullability.Equals(
                        foreignKey.Type,
                        principalKey.Type))
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            RelationshipKeyTypesMismatch,
                            location,
                            entity.Name,
                            navigation.Name,
                            foreignKeyName,
                            foreignKey.Type.ToDisplayString(),
                            principalKeyName,
                            principalKey.Type.ToDisplayString()));

                    valid = false;
                }
            }
        }

        return valid;
    }

    private static Location GetLocation(ISymbol symbol)
    {
        return symbol
                   .DeclaringSyntaxReferences
                   .FirstOrDefault()
                   ?.GetSyntax()
                   .GetLocation()
               ?? Location.None;
    }

    private static ITypeSymbol? GetNavigationTargetType(
        ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        if (type is INamedTypeSymbol named)
        {
            var enumerable =
                named.AllInterfaces.FirstOrDefault(i =>
                    i.OriginalDefinition.SpecialType ==
                    SpecialType.System_Collections_Generic_IEnumerable_T &&
                    i.TypeArguments.Length == 1);

            if (enumerable is not null)
                return enumerable.TypeArguments[0];
        }

        return type;
    }

    private static string IsCollectionExpression(
        ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
            return "true";

        if (type.AllInterfaces.Any(i =>
                i.OriginalDefinition.SpecialType ==
                SpecialType.System_Collections_Generic_IEnumerable_T))
        {
            return "true";
        }

        return "false";
    }
}