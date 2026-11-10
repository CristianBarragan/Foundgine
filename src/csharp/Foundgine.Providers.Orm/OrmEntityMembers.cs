using System.Diagnostics.CodeAnalysis;

namespace Foundgine.Providers.Orm;

/// <summary>
/// The entity members an ORM needs preserved to materialize and track entities. It matches the
/// <see cref="DynamicallyAccessedMembersAttribute"/> requirement EF Core places on
/// <c>DbContext.Set&lt;TEntity&gt;()</c>. Entity type parameters that reach an ORM carry it
/// (<c>[DynamicallyAccessedMembers(OrmEntityMembers.Required)] TEntity</c>) so trimming/Native AOT
/// keeps those members instead of the requirement being hidden behind a suppression.
/// </summary>
public static class OrmEntityMembers
{
    public const DynamicallyAccessedMemberTypes Required =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties |
        DynamicallyAccessedMemberTypes.Interfaces;
}
