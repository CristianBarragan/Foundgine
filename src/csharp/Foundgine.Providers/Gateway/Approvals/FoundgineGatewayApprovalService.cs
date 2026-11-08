using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Providers.Gateway.Execution;
using Foundgine.Runtime;

namespace Foundgine.Providers.Gateway.Approvals;

public sealed record FoundgineGatewayApprovalReceipt(string Handle, DateTimeOffset ExpiresAt, string PlanFingerprint);

/// <summary>Creates, revokes and consumes opaque, one-time approval handles.</summary>
public sealed class FoundgineGatewayApprovalService
{
    private readonly IFoundgineGatewayExecution _execution;
    private readonly IFoundgineGatewayApprovalStore _store;
    private readonly ISecurityExecutionContextProvider _security;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;

    public FoundgineGatewayApprovalService(IFoundgineGatewayExecution execution,
        IFoundgineGatewayApprovalStore store, ISecurityExecutionContextProvider security,
        TimeProvider? timeProvider = null, TimeSpan? lifetime = null)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _time = timeProvider ?? TimeProvider.System;
        _lifetime = lifetime ?? TimeSpan.FromMinutes(10);
        if (_lifetime <= TimeSpan.Zero || _lifetime > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Approval lifetime must be greater than zero and no more than 24 hours.");
    }

    public async ValueTask<FoundgineGatewayApprovalReceipt> CreateAsync(
        SemanticMutationOperationGraph graph, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var identity = RequireIdentity("gateway mutation approval");
        var approval = _execution.ApproveMutation(graph);
        if (approval.Request.Security is not { } approvedSecurity ||
            !string.Equals(approvedSecurity.Subject, identity.Subject, StringComparison.Ordinal) ||
            !string.Equals(approvedSecurity.Tenant, identity.Tenant, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The approval security context changed while the approval was being created.");
        var handle = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = _time.GetUtcNow().Add(_lifetime);
        await _store.StoreAsync(Digest(handle), approval, identity.Subject, identity.Tenant, expires, cancellationToken)
            .ConfigureAwait(false);
        return new FoundgineGatewayApprovalReceipt(handle, expires, approval.PlanFingerprint);
    }

    public async Task<MutationExecutionResult?> ExecuteAsync(string handle,
        CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        var identity = RequireIdentity("gateway approved mutation execution");
        var approval = await _store.TakeAsync(Digest(handle), identity.Subject, identity.Tenant,
            _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (approval is null) return null;
        return await _execution.ExecuteApprovedMutationAsync(approval, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> RevokeAsync(string handle, CancellationToken cancellationToken = default)
    {
        ValidateHandle(handle);
        var identity = RequireIdentity("gateway mutation approval revocation");
        return await _store.RevokeAsync(Digest(handle), identity.Subject, identity.Tenant, cancellationToken)
            .ConfigureAwait(false);
    }

    private (string Subject, string? Tenant) RequireIdentity(string operation)
    {
        var context = _security.RequireSecurityExecutionContext("Gateway", operation);
        return (context.Subject, context.Tenant);
    }

    private static void ValidateHandle(string handle)
    {
        if (handle is null || handle.Length != 43 || handle.Any(c =>
                !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new ArgumentException("Approval handle is invalid.", nameof(handle));
    }

    private static string Digest(string handle) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(handle)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
