using Foundgine.Core.Abstractions;

namespace Foundgine.Providers.Aot;

/// <summary>
/// Generated application-facing field handle. It carries the compact runtime
/// identity without exposing numeric construction to application code.
/// </summary>
public readonly record struct GeneratedSemanticField(EntityId Entity, FieldId Id, string Name);