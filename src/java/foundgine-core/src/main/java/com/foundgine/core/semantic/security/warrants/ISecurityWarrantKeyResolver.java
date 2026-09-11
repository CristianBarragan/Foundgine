package com.foundgine.core.semantic.security.warrants;
import java.security.interfaces.RSAPublicKey;
public interface ISecurityWarrantKeyResolver { RSAPublicKey resolve(String keyId); }
