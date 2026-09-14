using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class ExecutionAuthorizationRevalidationTests
{
    [Fact]
    public async Task Current_authority_version_mismatch_fails_closed()
    {
        var model = new SemanticModelBuilder()
            .Entity(new Foundgine.Core.Abstractions.EntityId(1), "Customer", entity => entity
                .Identity(new Foundgine.Core.Abstractions.FieldId(1), "Id")
                .Field(new Foundgine.Core.Abstractions.FieldId(1), "Id", typeof(long)))
            .Build();
        var contract = model.Freeze().CreateSnapshot();
        var evidence = new SemanticAuthorizationEvidence(
            contract.ContractFingerprint,
            "auth-v1",
            AuthorizationVersion: 7,
            AuthorizationAuthorityFingerprint: "authority-v7");

        var validator = new SemanticExecutionAuthorizationRevalidator();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await validator.ValidateAsync(
                contract,
                evidence,
                new ExecutionAuthorizationAuthorityState(8, "authority-v8")));
    }

    [Fact]
    public async Task Revoked_current_authority_fails_closed()
    {
        var model = new SemanticModelBuilder()
            .Entity(new Foundgine.Core.Abstractions.EntityId(1), "Customer", entity => entity
                .Identity(new Foundgine.Core.Abstractions.FieldId(1), "Id")
                .Field(new Foundgine.Core.Abstractions.FieldId(1), "Id", typeof(long)))
            .Build();
        var contract = model.Freeze().CreateSnapshot();
        var evidence = new SemanticAuthorizationEvidence(
            contract.ContractFingerprint,
            "auth-v1",
            AuthorizationVersion: 7,
            AuthorizationAuthorityFingerprint: "authority-v7");

        var validator = new SemanticExecutionAuthorizationRevalidator();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await validator.ValidateAsync(
                contract,
                evidence,
                new ExecutionAuthorizationAuthorityState(7, "authority-v7", Allowed: false)));
    }
}
