using Pulumi;
using PulumiCrossCloud.Core;

// Intentionally keep this in the Keycloak namespace alongside the stack
namespace PulumiCrossCloud.Keycloak;

/// <summary>
/// PulumiRealConfig encapsulates the native Pulumi.Keycloak arguments needed to
/// configure a realm with clients and identity providers.
/// </summary>
public sealed class PulumiRealmConfig
{
    /// <summary>
    /// The name of the realm to create. This will be used as the realm identifier.
    /// </summary>
    public required string RealmName { get; init; }

    /// <summary>
    /// The native realm arguments. Caller must populate required fields per provider (e.g., Enabled).
    /// The RealmName field will be automatically set from the RealmName property.
    /// </summary>
    public required Pulumi.Keycloak.RealmArgs RealmArgs { get; init; }

    /// <summary>
    /// A list of OpenID client arguments to create within the realm.
    /// The RealmId field will be overridden to reference the created realm.
    /// </summary>
    public List<Pulumi.Keycloak.OpenId.ClientArgs>? Clients { get; init; } = [];

    /// <summary>
    /// A list of OIDC identity provider arguments to create within the realm.
    /// The Realm field will be overridden to reference the created realm.
    /// </summary>
    public List<Pulumi.Keycloak.Oidc.IdentityProviderArgs>? IdentityProviders { get; init; } = [];

    /// <summary>
    /// Optional explicit provider configuration (e.g., to override URL or credentials).
    /// If not provided, the KeycloakStack will construct a provider from the compute URL
    /// and admin username/password.
    /// </summary>
    public Pulumi.Keycloak.ProviderArgs? ProviderOverrides { get; init; }

    /// <summary>
    /// Optional theme configuration for the realm.
    /// </summary>
    public RealmThemeConfig? ThemeConfig { get; init; }

    /// <summary>
    /// Optional user profile configuration for the realm.
    /// </summary>
    public Pulumi.Keycloak.RealmUserProfileArgs? UserProfile { get; init; }
}

/// <summary>
/// Configuration for Keycloak themes.
/// </summary>
public sealed class RealmThemeConfig
{
    /// <summary>
    /// The name of the login theme to use for this realm.
    /// </summary>
    public string? LoginTheme { get; init; }

    /// <summary>
    /// The name of the account theme to use for this realm.
    /// </summary>
    public string? AccountTheme { get; init; }

    /// <summary>
    /// The name of the admin theme to use for this realm.
    /// </summary>
    public string? AdminTheme { get; init; }

    /// <summary>
    /// The name of the email theme to use for this realm.
    /// </summary>
    public string? EmailTheme { get; init; }
}

/// <summary>
/// Contains all the information needed to configure OpenID Connect authentication on a server.
/// </summary>
public sealed class KeycloakClientResult
{
    /// <summary>
    /// The client ID for this OpenID Connect client.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// The client secret for this OpenID Connect client.
    /// Will be null for public clients
    /// </summary>
    public required ISecret? ClientSecret { get; init; }

    /// <summary>
    /// The realm name where this client is configured.
    /// </summary>
    public required string RealmName { get; init; }

    /// <summary>
    /// The Keycloak server URL (e.g., https://keycloak.example.com).
    /// </summary>
    public required string KeycloakUrl { get; init; }

    /// <summary>
    /// The OpenID Connect authorization endpoint URL.
    /// </summary>
    public required string AuthorizationEndpoint { get; init; }

    /// <summary>
    /// The OpenID Connect token endpoint URL.
    /// </summary>
    public required string TokenEndpoint { get; init; }

    /// <summary>
    /// The OpenID Connect userinfo endpoint URL.
    /// </summary>
    public required string UserInfoEndpoint { get; init; }

    /// <summary>
    /// The OpenID Connect end session endpoint URL.
    /// </summary>
    public required string EndSessionEndpoint { get; init; }

    /// <summary>
    /// The OpenID Connect JWKS endpoint URL.
    /// </summary>
    public required string JwksEndpoint { get; init; }

    /// <summary>
    /// The OpenID Connect issuer identifier.
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    /// The client's redirect URIs as configured in Keycloak.
    /// </summary>
    public required List<string> ValidRedirectUris { get; init; }

    /// <summary>
    /// The client's web origins as configured in Keycloak.
    /// </summary>
    public required List<string> WebOrigins { get; init; }

    /// <summary>
    /// The client's access type (e.g., "CONFIDENTIAL", "PUBLIC").
    /// </summary>
    public required string AccessType { get; init; }

    /// <summary>
    /// Whether the client requires client authentication.
    /// </summary>
    public required bool RequiresClientAuthentication { get; init; }
}

/// <summary>
/// Result of deploying a realm configuration, including created resources and client configuration results.
/// </summary>
public sealed class RealmDeploymentResult
{
    /// <summary>
    /// The created Keycloak realm.
    /// </summary>
    public required Pulumi.Keycloak.Realm Realm { get; init; }

    /// <summary>
    /// All created resources (realm, clients, identity providers).
    /// </summary>
    public required List<Resource> CreatedResources { get; init; }

    /// <summary>
    /// Dictionary mapping client names to their configuration results containing all information needed for OpenID Connect setup.
    /// </summary>
    public required Dictionary<string, Output<KeycloakClientResult>> ClientResults { get; init; }
}

/// <summary>
/// Helper to materialize the above configuration using a specific Keycloak provider.
/// </summary>
internal static class PulumiRealmDeployer
{
    public static RealmDeploymentResult Deploy(
        PulumiRealmConfig config,
        Pulumi.Keycloak.Provider provider,
        CloudResourceFactory? factory = null,
        string? keycloakUrl = null,
        IEnumerable<Resource>? dependsOn = null
    )
    {
        // Ensure realm name is set in the args
        config.RealmArgs.RealmName = config.RealmName;

        // Apply theme configuration if provided
        if (config.ThemeConfig is not null)
        {
            if (!string.IsNullOrWhiteSpace(config.ThemeConfig.LoginTheme))
                config.RealmArgs.LoginTheme = config.ThemeConfig.LoginTheme;
            if (!string.IsNullOrWhiteSpace(config.ThemeConfig.AccountTheme))
                config.RealmArgs.AccountTheme = config.ThemeConfig.AccountTheme;
            if (!string.IsNullOrWhiteSpace(config.ThemeConfig.AdminTheme))
                config.RealmArgs.AdminTheme = config.ThemeConfig.AdminTheme;
            if (!string.IsNullOrWhiteSpace(config.ThemeConfig.EmailTheme))
                config.RealmArgs.EmailTheme = config.ThemeConfig.EmailTheme;
        }

        // Create realm
        var realm = new Pulumi.Keycloak.Realm(
            $"{config.RealmName}-realm",
            config.RealmArgs,
            new CustomResourceOptions
            {
                Provider = provider,
                DependsOn = dependsOn?.ToArray() ?? []
            }
        );

        var created = new List<Resource> { realm };
        var clientResults = new Dictionary<string, Output<KeycloakClientResult>>();

        // Create user profile configuration
        if (config.UserProfile is not null)
        {
            // Ensure realm linkage
            config.UserProfile.RealmId = realm.Id;
            var userProfile = new Pulumi.Keycloak.RealmUserProfile(
                $"{config.RealmName}-user-profile",
                config.UserProfile,
                new CustomResourceOptions
                {
                    Provider = provider,
                    DependsOn = new Resource[] { realm }
                }
            );
            created.Add(userProfile);
        }

        // Create clients
        if (config.Clients is not null && config.Clients.Count > 0)
        {
            var index = 0;
            foreach (var clientArgs in config.Clients)
            {
                // Ensure realm linkage
                clientArgs.RealmId = realm.Id;
                var client = new Pulumi.Keycloak.OpenId.Client(
                    $"{config.RealmName}-client-{++index}",
                    clientArgs,
                    new CustomResourceOptions
                    {
                        Provider = provider,
                        DependsOn = dependsOn is null
                            ? new Resource[] { realm }
                            : new Resource[] { realm }
                                .Concat(dependsOn)
                                .ToArray()
                    }
                );
                created.Add(client);

                // If we have a factory and keycloakUrl, create comprehensive client configuration result
                if (factory is not null && keycloakUrl is not null)
                {
                    var clientIndex = index;
                    var redirectUrisOutput =
                        clientArgs.ValidRedirectUris != null
                            ? clientArgs.ValidRedirectUris.ToOutput().Apply(x => x.ToArray())
                            : Output.Create(new string[0]);
                    var webOriginsOutput =
                        clientArgs.WebOrigins != null
                            ? clientArgs.WebOrigins.ToOutput().Apply(x => x.ToArray())
                            : Output.Create(new string[0]);
                    var accessTypeOutput =
                        clientArgs.AccessType != null
                            ? clientArgs.AccessType.ToOutput()
                            : Output.Create("PUBLIC");

                    var clientResult = Output
                        .Tuple(
                            client.ClientId, // Output<string>
                            client.ClientSecret, // Output<string>
                            redirectUrisOutput, // Output<string[]>
                            webOriginsOutput, // Output<string[]>
                            accessTypeOutput // Output<string>
                        )
                        .Apply(t =>
                        {
                            var (cid, csec, redirectUrisArr, webOriginsArr, atRaw) = t;

                            // Default for preview/unknowns
                            var clientId = string.IsNullOrWhiteSpace(cid)
                                ? $"client-{clientIndex}"
                                : cid;
                            var clientSecretValue = csec ?? string.Empty;
                            var accessType = string.IsNullOrWhiteSpace(atRaw) ? "PUBLIC" : atRaw;

                            ISecret? secret = null;
                            var requiresSecret = string.Equals(
                                accessType,
                                "CONFIDENTIAL",
                                StringComparison.OrdinalIgnoreCase
                            );

                            // For CONFIDENTIAL clients, we need to create a secret
                            // For PUBLIC clients, we don't need a secret
                            if (requiresSecret)
                            {
                                // For CONFIDENTIAL clients, always create a secret using the Keycloak-generated secret
                                // Even if clientSecret appears empty initially, Keycloak will have generated one
                                secret = factory.CreateSecret(
                                    $"{config.RealmName}-{clientId}-secret-1",
                                    clientSecretValue,
                                    ignoreChangesAfterCreate: true // This is key - let Keycloak manage the actual value
                                );
                            }
                            else
                            {
                                // For PUBLIC clients, create an empty secret (some systems still expect the field to exist)
                            }

                            // Build OpenID Connect endpoints
                            var realmName = config.RealmName;
                            var baseUrl = keycloakUrl.TrimEnd('/');

                            return new KeycloakClientResult
                            {
                                ClientId = clientId,
                                ClientSecret = secret,
                                RealmName = realmName,
                                KeycloakUrl = baseUrl,
                                AuthorizationEndpoint =
                                    $"{baseUrl}/realms/{realmName}/protocol/openid-connect/auth",
                                TokenEndpoint =
                                    $"{baseUrl}/realms/{realmName}/protocol/openid-connect/token",
                                UserInfoEndpoint =
                                    $"{baseUrl}/realms/{realmName}/protocol/openid-connect/userinfo",
                                EndSessionEndpoint =
                                    $"{baseUrl}/realms/{realmName}/protocol/openid-connect/logout",
                                JwksEndpoint =
                                    $"{baseUrl}/realms/{realmName}/protocol/openid-connect/certs",
                                Issuer = $"{baseUrl}/realms/{realmName}",
                                ValidRedirectUris = redirectUrisArr?.ToList() ?? new List<string>(),
                                WebOrigins = webOriginsArr?.ToList() ?? new List<string>(),
                                AccessType = accessType,
                                RequiresClientAuthentication = accessType == "CONFIDENTIAL"
                            };
                        });

                    var clientKey = $"client-{index}";
                    clientResults[clientKey] = clientResult;
                }
            }
        }

        // Create identity providers
        if (config.IdentityProviders is not null && config.IdentityProviders.Count > 0)
        {
            var index = 0;
            foreach (var idpArgs in config.IdentityProviders)
            {
                // Ensure realm linkage
                idpArgs.Realm = realm.Id;
                var idp = new Pulumi.Keycloak.Oidc.IdentityProvider(
                    $"{config.RealmName}-idp-{++index}",
                    idpArgs,
                    new CustomResourceOptions
                    {
                        Provider = provider,
                        DependsOn = dependsOn is null
                            ? new Resource[] { realm }
                            : new Resource[] { realm }
                                .Concat(dependsOn)
                                .ToArray()
                    }
                );
                created.Add(idp);
            }
        }

        return new RealmDeploymentResult
        {
            Realm = realm,
            CreatedResources = created,
            ClientResults = clientResults
        };
    }
}
