using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.Keycloak.Admin;
using Foundgine.Extensions.Keycloak.Authentication;
using Foundgine.Extensions.Keycloak.Configuration;
using Foundgine.Extensions.Keycloak.Security;
using Foundgine.Extensions.Keycloak.Tokens;
using Foundgine.Extensions.Keycloak.Warrants;
using Foundgine.Extensions.Temporal.Security;
using Foundgine.Providers.Gateway.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Foundgine.Extensions.Keycloak.Hosting;

public sealed class FoundgineKeycloakBuilder
{
    internal FoundgineKeycloakBuilder(IServiceCollection services, FoundgineKeycloakOptions options)
    {
        Services = services;
        Options = options;
    }

    public IServiceCollection Services { get; }
    public FoundgineKeycloakOptions Options { get; }

    /// <summary>Adds an ASP.NET Core authorization policy requiring a Keycloak role (e.g. the Temporal approval policy).</summary>
    public FoundgineKeycloakBuilder AddRolePolicy(string policyName, string role)
    {
        Services.AddAuthorization(o => o.AddPolicy(policyName, p => p.RequireAuthenticatedUser().RequireRole(role)));
        return this;
    }

    /// <summary>Use a custom signing key source (KMS/HSM). Replaces the PEM/ephemeral provider.</summary>
    public FoundgineKeycloakBuilder UseWarrantSigningKeyProvider<TProvider>()
        where TProvider : class, IFoundgineWarrantSigningKeyProvider
    {
        Services.Replace(ServiceDescriptor.Singleton<IFoundgineWarrantSigningKeyProvider, TProvider>());
        return this;
    }

    /// <summary>Development only: signs with a per-process key. Not valid across instances or restarts.</summary>
    public FoundgineKeycloakBuilder UseEphemeralWarrantKey()
    {
        Services.Replace(ServiceDescriptor.Singleton<IFoundgineWarrantSigningKeyProvider>(
            _ => PemWarrantSigningKeyProvider.CreateEphemeral(Options.WarrantKeyId)));
        return this;
    }

    /// <summary>Makes Temporal activities run with <see cref="FoundgineKeycloakOptions.WorkerRoles"/> authority.</summary>
    public FoundgineKeycloakBuilder UseForTemporalWorker()
    {
        if (Options.WorkerRoles.Count == 0)
            throw new InvalidOperationException("Configure WorkerRoles before UseForTemporalWorker().");
        Services.Replace(ServiceDescriptor.Scoped<IFoundgineTemporalSecurityContextFactory, KeycloakTemporalSecurityContextFactory>());
        return this;
    }
}

public static class FoundgineKeycloakServiceCollectionExtensions
{
    /// <summary>
    /// API host: validates Keycloak bearer tokens, maps roles to ASP.NET roles, and registers the Keycloak-backed
    /// <see cref="ISecurityExecutionContextProvider"/> (replacing any previous one, e.g. the sample's HttpContext provider).
    /// </summary>
    public static FoundgineKeycloakBuilder AddFoundgineKeycloak(
        this IServiceCollection services, Action<FoundgineKeycloakOptions> configure)
    {
        var builder = services.AddFoundgineKeycloakWarrants(configure);
        builder.Options.ValidateAuthenticationSettings();

        KeycloakAuthentication.Add(services, builder.Options);
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<ISecurityExecutionContextProvider, KeycloakSecurityExecutionContextProvider>());
        return builder;
    }

    /// <summary>Binds <see cref="FoundgineKeycloakOptions"/> from a configuration section, then applies <paramref name="configure"/>.</summary>
    public static FoundgineKeycloakBuilder AddFoundgineKeycloak(
        this IServiceCollection services, IConfiguration section, Action<FoundgineKeycloakOptions>? configure = null) =>
        services.AddFoundgineKeycloak(o => { section.Bind(o); configure?.Invoke(o); });

    /// <summary>
    /// Warrant issuance only (no HTTP authentication): for worker and background hosts. Does not register an
    /// <see cref="ISecurityExecutionContextProvider"/>.
    /// </summary>
    public static FoundgineKeycloakBuilder AddFoundgineKeycloakWarrants(
        this IServiceCollection services, Action<FoundgineKeycloakOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FoundgineKeycloakOptions();
        configure(options);
        options.ValidateWarrantSettings();

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFoundgineWarrantSigningKeyProvider>(_ => CreateKeyProvider(options));
        services.TryAddSingleton<IFoundgineWarrantIssuer, FoundgineKeycloakWarrantIssuer>();
        // Verification side: trusts this host's own signing key plus the configured TrustedWarrantPublicKeys.
        services.TryAddSingleton<ISecurityWarrantKeyResolver>(sp =>
            FoundgineWarrantKeyResolver.Create(options, sp.GetRequiredService<IFoundgineWarrantSigningKeyProvider>()));
        services.AddHttpClient(nameof(KeycloakServiceTokenProvider));
        services.TryAddSingleton<KeycloakServiceTokenProvider>();
        services.AddHttpClient(nameof(KeycloakAdminClient));
        services.TryAddSingleton<KeycloakAdminClient>();
        services.AddFoundgineProvider<FoundgineKeycloakProvider>();
        return new FoundgineKeycloakBuilder(services, options);
    }

    private static PemWarrantSigningKeyProvider CreateKeyProvider(FoundgineKeycloakOptions o)
    {
        var pem = !string.IsNullOrWhiteSpace(o.WarrantPrivateKeyPem) ? o.WarrantPrivateKeyPem
            : !string.IsNullOrWhiteSpace(o.WarrantPrivateKeyPemPath) ? File.ReadAllText(o.WarrantPrivateKeyPemPath)
            : throw new InvalidOperationException(
                "No warrant signing key configured. Set WarrantPrivateKeyPem/WarrantPrivateKeyPemPath, " +
                "call UseWarrantSigningKeyProvider<T>(), or UseEphemeralWarrantKey() for development.");
        return new PemWarrantSigningKeyProvider(o.WarrantKeyId, pem);
    }
}
