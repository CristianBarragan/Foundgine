using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql;
using Xunit;
using BankingModel = Foundgine.E2E.Tests.Banking.BankingSemanticModel;

namespace Foundgine.E2E.Tests;

public sealed class StorageNameQuotingTests
{
    [Fact]
    public void Schema_qualified_storage_names_are_quoted_per_identifier()
    {
        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            BankingModel.Customer,
            "Customer",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "Name")
            ],
            StorageName: "Banking.Customer",
            Fields:
            [
                new FieldMetadata(
                    new FieldId(1),
                    "Id",
                    typeof(int),
                    new ColumnReference(BankingModel.Customer, new ColumnId(1))),
                new FieldMetadata(
                    new FieldId(2),
                    "Name",
                    typeof(string),
                    new ColumnReference(BankingModel.Customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(BankingModel.Customer, new ColumnId(1))));

        var request = new SemanticRequest(
            BankingModel.Customer,
            [new SemanticSelection(new FieldId(1), null, [])]);

        var resolved = new SemanticRequestResolver(BankingModel.Build().Freeze().CreateSnapshot()).Resolve(request);
        var authorized = new SemanticAuthorizer(
            new AllowAllSemanticAuthorizationPolicy()).Authorize(resolved);
        var plan = new Planner().Plan(authorized) with
        {
            AuthorizationBinding = new SemanticPlanAuthorizationBinding("test-contract", "test-authorization")
        };
        var sql = new SqlCompiler(metadata).Compile(plan).CommandText;

        Assert.Contains("FROM \"Banking\".\"Customer\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM \"Banking.Customer\"", sql, StringComparison.Ordinal);
    }
}