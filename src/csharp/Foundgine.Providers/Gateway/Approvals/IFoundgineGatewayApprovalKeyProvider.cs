namespace Foundgine.Providers.Gateway.Approvals;

/// <summary>Supplies AES-256 keys by id so approval payload keys can be rotated. Key material never leaves the host's key store.</summary>
public interface IFoundgineGatewayApprovalKeyProvider
{
    /// <summary>Id of the key used for new encryptions. Must match <c>[A-Za-z0-9._-]{1,64}</c>.</summary>
    string CurrentKeyId { get; }

    /// <summary>Returns the 32-byte key for <paramref name="keyId"/>, or throws if the key is unknown or retired.</summary>
    byte[] GetKey(string keyId);
}
