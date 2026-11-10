using System;
using Foundgine.Runtime;

namespace Foundgine.Providers.Gateway.Approvals;

/// <summary>
/// Encrypts approval payloads at rest (AES-256-GCM, see <see cref="ApprovalPayloadEnvelope"/>) by decorating the host
/// codec that supplies the AOT-safe serialization; no reflection-based serializer is chosen here. Keys are addressed by
/// id so they can be rotated while older approvals stay readable until they expire. Evidence is non-secret and passes
/// through unencrypted.
/// <para>Limit: the codec port does not receive the row's subject, tenant or digest, so the ciphertext is not bound to
/// them cryptographically; the store's lookup predicates enforce that binding.</para>
/// </summary>
public sealed class EncryptingFoundgineGatewayApprovalPayloadCodec : IFoundgineGatewayApprovalPayloadCodec
{
    private readonly IFoundgineGatewayApprovalPayloadCodec _inner;
    private readonly IFoundgineGatewayApprovalKeyProvider _keys;

    public EncryptingFoundgineGatewayApprovalPayloadCodec(IFoundgineGatewayApprovalPayloadCodec inner, IFoundgineGatewayApprovalKeyProvider keys)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public string EncodeApproval(MutationPlanApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var keyId = _keys.CurrentKeyId;
        ApprovalPayloadEnvelope.RequireKeyId(keyId);
        return ApprovalPayloadEnvelope.Seal(_inner.EncodeApproval(approval), keyId, _keys.GetKey(keyId));
    }

    public MutationPlanApproval DecodeApproval(string payload) =>
        _inner.DecodeApproval(ApprovalPayloadEnvelope.Open(payload, _keys));

    public string EncodeEvidence(FoundgineGatewayApprovalEvidence evidence) => _inner.EncodeEvidence(evidence);
}
