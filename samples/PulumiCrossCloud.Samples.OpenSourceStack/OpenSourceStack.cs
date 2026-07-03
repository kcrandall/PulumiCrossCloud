using Pulumi;
using Pulumi.Keycloak;
using PulumiCrossCloud.Aws;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Gcp;
using PulumiCrossCloud.Keycloak;

namespace PulumiCrossCloud.Samples.OpenSourceStack;

public class OpenSourceStack : Stack
{
    [Output]
    public Output<int> ClientLogHook { get; private set; } = null!;

    [Output]
    public Output<int> ClientResultCount { get; private set; } = null!;

    public OpenSourceStack()
    {
        // Example: Deploy a simple web app to all three clouds
        var gcpFactory = new GcpCloudResourceFactory();

        GcpProjectConfig.ProjectId = "pulumi-test-469022";

        var gcpDbUrlSecret = gcpFactory.CreateSecret(
            "keycloak-db-url",
            "jdbc:postgresql://ep-lingering-rice-a5p2as68-pooler.us-east-2.aws.neon.tech:5432/keycloak2?sslmode=require&channel_binding=require"
        );
        var gcpDbUserSecret = gcpFactory.CreateSecret("keycloak-db-user", "main_owner");
        var gcpDbPasswordSecret = gcpFactory.CreateSecret("keycloak-db-password", "rcEj7YHeBQO0");

        // Standard Keycloak realm configuration
        var realmConfig = new PulumiCrossCloud.Keycloak.PulumiRealmConfig
        {
            RealmName = "test2",
            RealmArgs = new RealmArgs
            {
                Enabled = true,
                DisplayName = "Open Source Stack Realm",
                DisplayNameHtml =
                    "<div class=\"kc-logo-text\"><span>Open Source Stack</span></div>",
                LoginWithEmailAllowed = true,
                RegistrationAllowed = true,
                DuplicateEmailsAllowed = false,
                ResetPasswordAllowed = true,
                RememberMe = true
            },
            ThemeConfig = new RealmThemeConfig { LoginTheme = "tcg-app-theme", },
            Clients = new List<Pulumi.Keycloak.OpenId.ClientArgs>
            {
                // JWT API Client (confidential client for backend services)
                new Pulumi.Keycloak.OpenId.ClientArgs
                {
                    ClientId = "oss-api-client",
                    Name = "OSS API Client",
                    Enabled = true,
                    ClientAuthenticatorType = "client-secret",
                    AccessType = "CONFIDENTIAL",
                    StandardFlowEnabled = false,
                    DirectAccessGrantsEnabled = true,
                    ServiceAccountsEnabled = true,
                    ConsentRequired = false,
                    FullScopeAllowed = true
                },
                // Web App Client (public client for frontend applications)
                new Pulumi.Keycloak.OpenId.ClientArgs
                {
                    ClientId = "oss-web-app",
                    Name = "OSS Web Application",
                    Enabled = true,
                    AccessType = "PUBLIC",
                    StandardFlowEnabled = true,
                    ImplicitFlowEnabled = false,
                    DirectAccessGrantsEnabled = false,
                    ServiceAccountsEnabled = false,
                    ValidRedirectUris = new[]
                    {
                        "http://localhost:3000/*",
                        "http://localhost:5174/*",
                        "https://app.oss-stack.com/*",
                        "https://app.oss-stack.com/callback"
                    },
                    WebOrigins = new[] { "http://localhost:3000", "https://app.oss-stack.com" },
                    ConsentRequired = false,
                    FullScopeAllowed = true,
                    RootUrl = "https://app.oss-stack.com",
                    AdminUrl = "https://app.oss-stack.com/admin"
                }
            },
            IdentityProviders = new List<Pulumi.Keycloak.Oidc.IdentityProviderArgs>
            {
                // new Pulumi.Keycloak.Oidc.IdentityProviderArgs
                // {
                //     Alias = "google",
                //     DisplayName = "Google",
                //     Enabled = true,
                //     ProviderId = "google",
                //     ExtraConfig = new Dictionary<string, string>
                //     {
                //         { "clientId", "your-google-client-id" },
                //         { "clientSecret", "your-google-client-secret" },
                //         { "defaultScope", "openid profile email" },
                //         { "useJwksUrl", "true" },
                //         { "jwksUrl", "https://www.googleapis.com/oauth2/v3/certs" },
                //         { "issuer", "https://accounts.google.com" }
                //     }
                // }
            }
        };

        // Configure theme providers (JAR files from Keycloakify build)
        var themeProviders = new KeycloakThemeProviders
        {
            ThemeJarFiles =
            [
                // Path to the Keycloakify-generated JAR file
                // This would typically be built from frontend/keycloaktheme/dist_keycloak/
                "../../frontend/keycloaktheme/dist_keycloak/keycloak-theme-for-kc-22-to-25.jar"
                // You can add multiple theme JAR files here:
                // "path/to/another-theme.jar"
            ]
            // CustomThemesDirectory = "path/to/custom/themes" // Alternative to JAR files
        };

        var keycloakStack = new KeycloakStack(
            gcpFactory,
            "oss-keycloak",
            gcpDbUrlSecret,
            gcpDbUserSecret,
            gcpDbPasswordSecret,
            realmConfig,
            themeProviders
        );

        ClientResultCount = keycloakStack.RealmResults.Apply(r => r.ClientResults.Count);

        // Example: Access the created client configuration results after deployment
        ClientLogHook = keycloakStack.RealmResults.Apply(rr =>
        {
            var dict = rr.ClientResults;
            var hooks = new List<Output<int>>();

            foreach (var (_, clientResult) in dict)
            {
                hooks.Add(
                    clientResult.Apply(cr =>
                    {
                        Log.Info(
                            $"Client '{cr.ClientId}' in realm '{cr.RealmName}'\n"
                                + $"  Keycloak URL: {cr.KeycloakUrl}\n"
                                + $"  Auth: {cr.AuthorizationEndpoint}\n"
                                + $"  Token: {cr.TokenEndpoint}\n"
                                + $"  JWKS: {cr.JwksEndpoint}\n"
                                + $"  Issuer: {cr.Issuer}\n"
                                + $"  AccessType: {cr.AccessType}  RequiresAuth: {cr.RequiresClientAuthentication}\n"
                                + $"  Redirect URIs: {string.Join(", ", cr.ValidRedirectUris)}\n"
                                + $"  Web Origins: {string.Join(", ", cr.WebOrigins)}"
                        );
                        return 0;
                    })
                );
            }

            return Output.All(hooks.ToArray()).Apply(_ => 0);
        });
    }
}
