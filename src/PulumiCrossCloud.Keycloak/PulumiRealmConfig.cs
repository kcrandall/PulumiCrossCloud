using Pulumi;
using Pulumi.Keycloak;

// Intentionally keep this in the Keycloak namespace alongside the stack
namespace PulumiCrossCloud.Keycloak;

/// <summary>
/// PulumiRealConfig encapsulates the native Pulumi.Keycloak arguments needed to
/// configure a realm with clients and identity providers.
/// </summary>
public sealed class PulumiRealConfig
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
    public List<Pulumi.Keycloak.OpenId.ClientArgs> Clients { get; init; } = [];

    /// <summary>
    /// A list of OIDC identity provider arguments to create within the realm.
    /// The Realm field will be overridden to reference the created realm.
    /// </summary>
    public List<Pulumi.Keycloak.Oidc.IdentityProviderArgs> IdentityProviders { get; init; } = [];

    /// <summary>
    /// Optional explicit provider configuration (e.g., to override URL or credentials).
    /// If not provided, the KeycloakStack will construct a provider from the compute URL
    /// and admin username/password.
    /// </summary>
    public Pulumi.Keycloak.ProviderArgs? ProviderOverrides { get; init; }
}

/// <summary>
/// Helper to materialize the above configuration using a specific Keycloak provider.
/// </summary>
internal static class PulumiRealmDeployer
{
    public static (Pulumi.Keycloak.Realm realm, List<Resource> created) Deploy(
        PulumiRealConfig config,
        Pulumi.Keycloak.Provider provider
    )
    {
        // Ensure realm name is set in the args
        config.RealmArgs.RealmName = config.RealmName;

        // Create realm
        var realm = new Pulumi.Keycloak.Realm(
            $"{config.RealmName}-realm",
            config.RealmArgs,
            new CustomResourceOptions { Provider = provider }
        );

        var created = new List<Resource> { realm };

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
                    new CustomResourceOptions { Provider = provider, DependsOn = [realm] }
                );
                created.Add(client);
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
                    new CustomResourceOptions { Provider = provider, DependsOn = [realm] }
                );
                created.Add(idp);
            }
        }

        return (realm, created);
    }
}
