using System.Linq.Expressions;
using Foundgine.Providers.Aot;

namespace Foundgine.E2E.Tests;

/// <summary>
/// A small AOT fixture owned by the E2E test project so the generated metadata
/// used by  contains the authorization being exercised by the SQL execution tests.
/// </summary>
[FoundgineEntity(Id = 3, StorageName = "contracts")]
public sealed class Contract
{
    [FoundgineField(Id = 1, StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(Id = 2, StorageName = "contract_type")]
    public int ContractType { get; init; }

    [FoundgineField(Id = 3, StorageName = "tenant_id")]
    public int TenantId { get; init; }
}

public sealed class UserContext
{
    public int TenantId { get; init; }
}

public static class Authorization
{
    [FoundgineAuthorization(10, Id = 10, Name = "CanVisitContract")]
    public static Expression<Func<UserContext, Contract, bool>> CanVisitContract =>
        (user, contract) => user.TenantId == contract.TenantId;
}