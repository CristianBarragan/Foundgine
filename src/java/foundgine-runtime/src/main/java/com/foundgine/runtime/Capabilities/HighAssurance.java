package com.foundgine.runtime.capabilities;

import com.foundgine.core.semantic.security.warrants.*;
import com.foundgine.runtime.*;
import java.nio.file.Path;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.PrivateKey;
import java.security.interfaces.RSAPrivateKey;
import java.security.interfaces.RSAPublicKey;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Development capability that supplies warrant verification defaults.
 *
 * <p>
 * The generated key material is process-local and therefore intentionally
 * unsuitable for production. Production applications should configure a
 * durable/trusted key resolver and replay store before enabling this
 * capability.
 */
public final class HighAssurance implements IFoundgineCapability {
	public static final String DEV_ISSUER = "foundgine-dev";

	@Override
	public void configure(FoundgineCapabilityContext context) {
		FoundgineOptions options = context.options();
		if (options.warrantKeyResolver() == null)
			options.warrantKeyResolver(new EphemeralDevWarrantKeyResolver());
		if (options.expectedWarrantIssuer() == null)
			options.expectedWarrantIssuer(DEV_ISSUER);
		if (options.warrantReplayStore() == null)
			options.warrantReplayStore(new FileSecurityWarrantReplayStore(
					Path.of(System.getProperty("java.io.tmpdir"), "foundgine-dev-warrant-replay.log").toString()));
	}

	/** Process-local development key resolver. */
	static final class EphemeralDevWarrantKeyResolver implements ISecurityWarrantKeyResolver {
		private final ConcurrentHashMap<String, KeyPair> keys = new ConcurrentHashMap<>();

		@Override
		public java.security.interfaces.RSAPublicKey resolve(String keyId) {
			KeyPair pair = keys.computeIfAbsent(keyId, ignored -> generate());
			return (java.security.interfaces.RSAPublicKey) pair.getPublic();
		}

		private static KeyPair generate() {
			try {
				KeyPairGenerator generator = KeyPairGenerator.getInstance("RSA");
				generator.initialize(2048);
				return generator.generateKeyPair();
			} catch (Exception e) {
				throw new IllegalStateException("Unable to create development RSA key.", e);
			}
		}
	}
}
