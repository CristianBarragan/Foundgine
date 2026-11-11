using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.Providers.Orm;
using Xunit;

namespace Foundgine.Orm.Tests;

/// <summary>
/// Pins the package layout: the EF Core provider ships as the standalone Foundgine.Providers.EntityFrameworkCore
/// assembly and the shared ORM core as Foundgine.Providers.Orm. Foundgine.Providers excludes both source folders
/// (see its csproj), so these assertions fail if either is merged back into another assembly by accident.
/// </summary>
public sealed class ProvidersPackageContentsTests
{
    [Fact]
    public void EfCoreProviderIsItsOwnAssembly()
    {
        var ef = typeof(FoundgineEfReader).Assembly;
        Assert.Equal("Foundgine.Providers.EntityFrameworkCore", ef.GetName().Name);
        Assert.Same(ef, typeof(FoundgineEfRepository<>).Assembly);
        Assert.Same(ef, typeof(EfCoreExecutionProvider).Assembly);
    }

    [Fact]
    public void OrmCoreIsItsOwnAssembly()
    {
        var orm = typeof(OrmPlanCompiler).Assembly;
        Assert.Equal("Foundgine.Providers.Orm", orm.GetName().Name);
        Assert.NotSame(typeof(FoundgineEfReader).Assembly, orm);
    }
}