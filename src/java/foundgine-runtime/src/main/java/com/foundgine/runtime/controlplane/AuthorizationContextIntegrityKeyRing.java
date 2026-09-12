package com.foundgine.runtime.controlplane;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.nio.charset.StandardCharsets;
import java.security.InvalidKeyException;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.NoSuchElementException;
import java.util.Objects;
import java.util.Set;
import java.util.UUID;

/**
 * Port of
 * {@code Foundgine.Runtime.ControlPlane.AuthorizationContextIntegrityKeyRing}.
 *
 * <p>
 * Immutable verification/signing key-ring snapshot. Makes lifecycle state
 * explicit: exactly one key is active for new evidence, verification-only keys
 * may authenticate existing evidence, and retired keys are unavailable. The key
 * material never leaves process memory.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>{@code HMACSHA256.HashData(key, data)} is ported via
 * {@code javax.crypto.Mac.getInstance("HmacSHA256")}.</li>
 * <li>{@code CryptographicOperations.FixedTimeEquals} is ported as
 * {@link MessageDigest#isEqual}, which the JDK guarantees runs in time
 * independent of where the first mismatched byte occurs.</li>
 * <li>{@code Convert.ToHexString}/{@code FromHexString} are ported via
 * {@link HexFormat}, lowercasing to match C#'s {@code .ToLowerInvariant()}
 * call.</li>
 * <li>{@code Guid} is ported as {@link UUID}; {@code actorId.ToString("D")}
 * (lowercase, dash-separated) matches {@link UUID#toString()} exactly, so no
 * reformatting is needed.</li>
 * <li>{@code IReadOnlySet<string>} is ported as {@code Set<String>}.</li>
 * </ul>
 */
public final class AuthorizationContextIntegrityKeyRing {
	private static final String ALGORITHM_VERSION = "HMAC-SHA256/v1";
	private static final HexFormat HEX = HexFormat.of();

	private final Map<String, byte[]> keys;
	private final Map<String, AuthorizationIntegrityKeyState> states;
	private final String activeKeyId;
	private final long configurationVersion;
	private final long lastRotationSequence;

	public AuthorizationContextIntegrityKeyRing(AuthorizationContextIntegrityKey activeKey,
			List<AuthorizationContextIntegrityKey> verificationKeys, long configurationVersion,
			long lastRotationSequence) {
		Objects.requireNonNull(activeKey, "activeKey");
		if (configurationVersion <= 0) {
			throw new IllegalArgumentException("configurationVersion must be positive.");
		}
		if (lastRotationSequence < 0) {
			throw new IllegalArgumentException("lastRotationSequence must not be negative.");
		}

		var keys = new LinkedHashMap<String, byte[]>();
		var states = new LinkedHashMap<String, AuthorizationIntegrityKeyState>();
		keys.put(activeKey.keyId(), activeKey.key());
		states.put(activeKey.keyId(), AuthorizationIntegrityKeyState.ACTIVE);

		if (verificationKeys != null) {
			for (var key : verificationKeys) {
				Objects.requireNonNull(key, "verificationKeys entry");
				if (key.keyId().equals(activeKey.keyId())) {
					if (!MessageDigest.isEqual(keys.get(key.keyId()), key.key())) {
						throw new IllegalStateException(
								"Integrity key id '" + key.keyId() + "' is configured with different key material.");
					}
					continue;
				}

				var existing = keys.get(key.keyId());
				if (existing != null && !MessageDigest.isEqual(existing, key.key())) {
					throw new IllegalStateException(
							"Integrity key id '" + key.keyId() + "' is configured with different key material.");
				}

				keys.put(key.keyId(), key.key());
				states.put(key.keyId(), AuthorizationIntegrityKeyState.VERIFICATION_ONLY);
			}
		}

		this.keys = keys;
		this.states = states;
		this.activeKeyId = activeKey.keyId();
		this.configurationVersion = configurationVersion;
		this.lastRotationSequence = lastRotationSequence;
	}

	public AuthorizationContextIntegrityKeyRing(AuthorizationContextIntegrityKey activeKey) {
		this(activeKey, List.of(), 1, 0);
	}

	private AuthorizationContextIntegrityKeyRing(String activeKeyId, Map<String, byte[]> keys,
			Map<String, AuthorizationIntegrityKeyState> states, long configurationVersion, long lastRotationSequence) {
		this.keys = copyKeys(keys);
		this.states = new LinkedHashMap<>(states);
		this.activeKeyId = activeKeyId;
		this.configurationVersion = configurationVersion;
		this.lastRotationSequence = lastRotationSequence;
	}

	private static Map<String, byte[]> copyKeys(Map<String, byte[]> source) {
		var copy = new LinkedHashMap<String, byte[]>();
		for (var entry : source.entrySet()) {
			copy.put(entry.getKey(), entry.getValue().clone());
		}
		return copy;
	}

	public String activeKeyId() {
		return activeKeyId;
	}

	public long configurationVersion() {
		return configurationVersion;
	}

	public long lastRotationSequence() {
		return lastRotationSequence;
	}

	public static String currentAlgorithmVersion() {
		return ALGORITHM_VERSION;
	}

	public List<AuthorizationIntegrityKeyDescriptor> describeKeys() {
		return states.entrySet().stream().map(e -> new AuthorizationIntegrityKeyDescriptor(e.getKey(), e.getValue()))
				.sorted((a, b) -> a.keyId().compareTo(b.keyId())).toList();
	}

	public AuthorizationIntegrityKeyState getState(String keyId) {
		var state = states.get(keyId);
		if (state == null) {
			throw new NoSuchElementException("Integrity key id '" + keyId + "' is not configured.");
		}
		return state;
	}

	public boolean canVerify(String keyId) {
		var state = states.get(keyId);
		return state != null && state != AuthorizationIntegrityKeyState.RETIRED;
	}

	/**
	 * Creates the next immutable ring snapshot. The previous active key becomes
	 * verification-only. Rotation requires a strictly increasing provenance
	 * sequence, preventing concurrent/replayed rotation commands.
	 */
	public AuthorizationContextIntegrityKeyRing rotate(AuthorizationContextIntegrityKey newActiveKey,
			AuthorizationKeyRotationProvenance provenance) {
		Objects.requireNonNull(newActiveKey, "newActiveKey");
		validateRotationProvenance(provenance);

		if (provenance.rotationSequence() <= lastRotationSequence) {
			throw new IllegalStateException("Key rotation sequence is stale. Last=" + lastRotationSequence
					+ ", requested=" + provenance.rotationSequence() + ".");
		}

		var existingState = states.get(newActiveKey.keyId());
		if (existingState != null) {
			if (existingState == AuthorizationIntegrityKeyState.RETIRED) {
				throw new IllegalStateException(
						"Retired integrity key '" + newActiveKey.keyId() + "' cannot be reactivated.");
			}
			var existing = keys.get(newActiveKey.keyId());
			if (existing == null || !MessageDigest.isEqual(existing, newActiveKey.key())) {
				throw new IllegalStateException(
						"Integrity key id '" + newActiveKey.keyId() + "' is configured with different key material.");
			}
		}

		var newKeys = copyKeys(keys);
		var newStates = new LinkedHashMap<>(states);
		newKeys.putIfAbsent(newActiveKey.keyId(), newActiveKey.key());

		newStates.put(activeKeyId, AuthorizationIntegrityKeyState.VERIFICATION_ONLY);
		newStates.put(newActiveKey.keyId(), AuthorizationIntegrityKeyState.ACTIVE);

		return new AuthorizationContextIntegrityKeyRing(newActiveKey.keyId(), newKeys, newStates,
				configurationVersion + 1, provenance.rotationSequence());
	}

	/**
	 * Retires a non-active key only after the caller supplies the set of key ids
	 * still referenced by persisted evidence. This makes premature retirement a
	 * hard failure rather than an availability accident.
	 */
	public AuthorizationContextIntegrityKeyRing retire(String keyId, AuthorizationKeyRotationProvenance provenance,
			Set<String> persistedKeyIds) {
		validateRotationProvenance(provenance);
		Objects.requireNonNull(persistedKeyIds, "persistedKeyIds");

		if (provenance.rotationSequence() <= lastRotationSequence) {
			throw new IllegalStateException("Key lifecycle sequence is stale. Last=" + lastRotationSequence
					+ ", requested=" + provenance.rotationSequence() + ".");
		}
		var state = states.get(keyId);
		if (state == null) {
			throw new NoSuchElementException("Integrity key id '" + keyId + "' is not configured.");
		}
		if (state == AuthorizationIntegrityKeyState.ACTIVE || keyId.equals(activeKeyId)) {
			throw new IllegalStateException("The active integrity key cannot be retired.");
		}
		if (persistedKeyIds.contains(keyId)) {
			throw new IllegalStateException(
					"Integrity key '" + keyId + "' is still referenced by persisted authorization evidence.");
		}

		var newKeys = copyKeys(keys);
		var newStates = new LinkedHashMap<>(states);
		newStates.put(keyId, AuthorizationIntegrityKeyState.RETIRED);

		return new AuthorizationContextIntegrityKeyRing(activeKeyId, newKeys, newStates, configurationVersion + 1,
				provenance.rotationSequence());
	}

	private void validateRotationProvenance(AuthorizationKeyRotationProvenance provenance) {
		Objects.requireNonNull(provenance, "provenance");
		if (provenance.operatorId().equals(AuthorizationKeyRotationProvenance.EMPTY_OPERATOR_ID)) {
			throw new SecurityException("Key rotation requires an identified operator.");
		}
		if (provenance.rotationSequence() <= 0) {
			throw new IllegalArgumentException("Key rotation sequence must be positive.");
		}
		if (provenance.credentialFingerprint() == null || provenance.credentialFingerprint().isBlank()) {
			throw new SecurityException("Key rotation credential provenance is required.");
		}
	}

	public String computeContextTag(UUID actorId, int tenantId, boolean allowed, long version, String fingerprint) {
		return computeTag("context", actorId, tenantId, allowed, version, fingerprint, activeKeyId);
	}

	public boolean verifyContextTag(UUID actorId, int tenantId, boolean allowed, long version, String fingerprint,
			String algorithmVersion, String keyId, String tag) {
		return verify("context", actorId, tenantId, allowed, version, fingerprint, algorithmVersion, keyId, tag);
	}

	public String computeTombstoneTag(UUID actorId, int tenantId, long version, String fingerprint) {
		return computeTag("tombstone", actorId, tenantId, false, version, fingerprint, activeKeyId);
	}

	public boolean verifyTombstoneTag(UUID actorId, int tenantId, long version, String fingerprint,
			String algorithmVersion, String keyId, String tag) {
		return verify("tombstone", actorId, tenantId, false, version, fingerprint, algorithmVersion, keyId, tag);
	}

	private String computeTag(String recordType, UUID actorId, int tenantId, boolean allowed, long version,
			String fingerprint, String keyId) {
		var state = states.get(keyId);
		if (state != AuthorizationIntegrityKeyState.ACTIVE) {
			throw new IllegalStateException(
					"Integrity key '" + keyId + "' is not active for new authorization evidence.");
		}

		var key = keys.get(keyId);
		var payload = canonicalize(recordType, actorId, tenantId, allowed, version, fingerprint, ALGORITHM_VERSION,
				keyId);
		var mac = hmacSha256(key, payload.getBytes(StandardCharsets.UTF_8));
		return HEX.formatHex(mac);
	}

	private boolean verify(String recordType, UUID actorId, int tenantId, boolean allowed, long version,
			String fingerprint, String algorithmVersion, String keyId, String tag) {
		if (!ALGORITHM_VERSION.equals(algorithmVersion) || keyId == null || keyId.isBlank() || tag == null
				|| tag.isBlank() || !canVerify(keyId)) {
			return false;
		}
		var key = keys.get(keyId);
		if (key == null) {
			return false;
		}

		var payload = canonicalize(recordType, actorId, tenantId, allowed, version, fingerprint, algorithmVersion,
				keyId);
		var expected = hmacSha256(key, payload.getBytes(StandardCharsets.UTF_8));
		try {
			var supplied = HEX.parseHex(tag);
			return MessageDigest.isEqual(expected, supplied);
		} catch (IllegalArgumentException e) {
			return false;
		}
	}

	private static byte[] hmacSha256(byte[] key, byte[] data) {
		try {
			var mac = Mac.getInstance("HmacSHA256");
			mac.init(new SecretKeySpec(key, "HmacSHA256"));
			return mac.doFinal(data);
		} catch (NoSuchAlgorithmException | InvalidKeyException e) {
			throw new IllegalStateException("HmacSHA256 is not available.", e);
		}
	}

	private static String canonicalize(String recordType, UUID actorId, int tenantId, boolean allowed, long version,
			String fingerprint, String algorithmVersion, String keyId) {
		if (recordType == null || recordType.isBlank() || fingerprint == null || fingerprint.isBlank()) {
			throw new IllegalArgumentException("Integrity payload contains a required empty field.");
		}

		// Length-prefixing avoids delimiter ambiguity and makes the representation
		// independent of locale, JSON ordering, or serializer behavior.
		return String.join("|", encode(recordType), encode(algorithmVersion), encode(keyId), encode(actorId.toString()),
				encode(Integer.toString(tenantId)), encode(allowed ? "1" : "0"), encode(Long.toString(version)),
				encode(fingerprint));
	}

	private static String encode(String value) {
		return value.length() + ":" + value;
	}
}
