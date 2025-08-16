using Pulumi;
using Pulumi.Keycloak;
using PulumiCrossCloud.Aws;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Gcp;
using PulumiCrossCloud.Keycloak;

namespace PulumiCrossCloud.Samples.OpenSourceStack;

public class OpenSourceStack
{
    public OpenSourceStack()
    {
        // Example: Deploy a simple web app to all three clouds
        var gcpFactory = new GcpCloudResourceFactory();

        GcpProjectConfig.ProjectId = "pulumi-test-469022";

        var gcpDbUrlSecret = gcpFactory.CreateSecret(
            "keycloak-db-url",
            "jdbc:postgresql://ep-lingering-rice-a5p2as68-pooler.us-east-2.aws.neon.tech:5432/keycloak?sslmode=require&channel_binding=require"
        );
        var gcpDbUserSecret = gcpFactory.CreateSecret("keycloak-db-user", "main_owner");
        var gcpDbPasswordSecret = gcpFactory.CreateSecret("keycloak-db-password", "rcEj7YHeBQO0");

        // Standard Keycloak realm configuration
        var realmConfig = new PulumiCrossCloud.Keycloak.PulumiRealConfig
        {
            RealmName = "oss-realm",
            RealmArgs = new RealmArgs
            {
                Enabled = true,
                DisplayName = "Open Source Stack Realm",
                DisplayNameHtml =
                    "<div class=\"kc-logo-text\"><span>Open Source Stack</span></div>",
                LoginWithEmailAllowed = false,
                DuplicateEmailsAllowed = false,
                ResetPasswordAllowed = true,
                RememberMe = true
            },
            Clients = new List<Pulumi.Keycloak.OpenId.ClientArgs>
            {
                // JWT API Client (confidential client for backend services)
                new Pulumi.Keycloak.OpenId.ClientArgs
                {
                    ClientId = "oss-api-client",
                    Name = "OSS API Client",
                    Enabled = true,
                    ClientAuthenticatorType = "client-secret",
                    ClientSecret = "oss-api-secret-change-in-production",
                    AccessType = "CONFIDENTIAL",
                    StandardFlowEnabled = false,
                    DirectAccessGrantsEnabled = true,
                    ServiceAccountsEnabled = true,
                    ValidRedirectUris = new[]
                    {
                        "http://localhost:3000/*",
                        "https://api.oss-stack.com/*"
                    },
                    WebOrigins = new[] { "http://localhost:3000", "https://api.oss-stack.com" },
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
                        "https://app.oss-stack.com/*",
                        "http://localhost:3000/callback",
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

        var keycloakStack = new KeycloakStack(
            gcpFactory,
            "oss-keycloak",
            gcpDbUrlSecret,
            gcpDbUserSecret,
            gcpDbPasswordSecret,
            realmConfig
        );
    }
}
