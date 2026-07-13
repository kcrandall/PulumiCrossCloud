using Pulumi;
using Pulumi.Keycloak.Oidc;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Keycloak;

/// <summary>
/// Static helper methods for constructing IdentityProviderArgs for common OIDC providers.
/// Use these when you need a non-sealed IdentityProviderArgs instance for Apple or Google.
/// </summary>
public static class IdentityProviderArgsFactory
{
    /// <summary>
    /// Creates an IdentityProviderArgs for Apple OIDC provider.
    /// </summary>
    /// <param name="alias">The alias displayed in Keycloak for this provider (e.g., "apple").</param>
    /// <param name="clientId">The Apple Services ID (client_id).</param>
    /// <param name="teamId">Your Apple Developer Team ID.</param>
    /// <param name="keyId">The Key ID for your Sign in with Apple key.</param>
    /// <param name="privateKey">The private key material used to sign the JWT client secret.</param>
    /// <param name="defaultScopes">Optional default scopes. Defaults to "name email".</param>
    /// <param name="enabled">Whether the provider is enabled. Defaults to true.</param>
    /// <returns>IdentityProviderArgs configured for Apple OIDC.</returns>
    public static IdentityProviderArgs CreateAppleIdentityProviderArgs(
        string alias,
        string clientId,
        string teamId,
        string keyId,
        ISecret privateKey,
        string? defaultScopes = null,
        bool enabled = true
    )
    {
        var clientSecretJwt = privateKey.Value.Apply(pk =>
            AppleJwtHelper.GenerateAppleClientSecretJwt(clientId, teamId, keyId, pk ?? string.Empty)
        );
        return new IdentityProviderArgs
        {
            Alias = alias,
            ProviderId = "apple",
            Enabled = enabled,
            ClientId = clientId,
            ClientSecret = clientSecretJwt,
            DefaultScopes = string.IsNullOrWhiteSpace(defaultScopes) ? "name email" : defaultScopes,
            AuthorizationUrl = "https://appleid.apple.com/auth/authorize",
            TokenUrl = "https://appleid.apple.com/auth/token",
            ExtraConfig = new InputMap<string> { { "teamId", teamId }, { "keyId", keyId } },
        };
    }

    /// <summary>
    /// Creates an IdentityProviderArgs for Google OIDC provider.
    /// </summary>
    /// <param name="alias">The alias displayed in Keycloak for this provider (e.g., "google").</param>
    /// <param name="clientId">The Google OAuth client ID.</param>
    /// <param name="clientSecret">The secret backing the Google OAuth client secret.</param>
    /// <param name="defaultScopes">Optional default scopes to request. Defaults to "openid profile email".</param>
    /// <param name="enabled">Whether the provider is enabled. Defaults to true.</param>
    /// <returns>IdentityProviderArgs configured for Google OIDC.</returns>
    public static IdentityProviderArgs CreateGoogleIdentityProviderArgs(
        string alias,
        string clientId,
        ISecret clientSecret,
        string? defaultScopes = null,
        bool enabled = true
    )
    {
        return new IdentityProviderArgs
        {
            Alias = alias,
            ProviderId = "google",
            Enabled = enabled,
            ClientId = clientId,
            ClientSecret = clientSecret.Value.Apply(v => v ?? string.Empty),
            DefaultScopes = string.IsNullOrWhiteSpace(defaultScopes)
                ? "openid profile email"
                : defaultScopes,
            AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenUrl = "https://oauth2.googleapis.com/token",
        };
    }

    /// <summary>
    /// Creates an IdentityProviderArgs for Microsoft (Azure AD / Microsoft Account) OIDC provider.
    /// </summary>
    /// <param name="alias">The alias displayed in Keycloak for this provider (e.g., "microsoft").</param>
    /// <param name="clientId">The Microsoft/Azure AD OAuth client ID.</param>
    /// <param name="clientSecret">The secret backing the Microsoft/Azure AD OAuth client secret.</param>
    /// <param name="defaultScopes">Optional default scopes to request. Defaults to "openid profile email".</param>
    /// <param name="enabled">Whether the provider is enabled. Defaults to true.</param>
    /// <returns>IdentityProviderArgs configured for Microsoft OIDC.</returns>
    public static IdentityProviderArgs CreateMicrosoftIdentityProviderArgs(
        string alias,
        string clientId,
        ISecret clientSecret,
        string? defaultScopes = null,
        bool enabled = true
    )
    {
        return new IdentityProviderArgs
        {
            Alias = alias,
            ProviderId = "microsoft",
            Enabled = enabled,
            ClientId = clientId,
            ClientSecret = clientSecret.Value.Apply(v => v ?? string.Empty),
            DefaultScopes = string.IsNullOrWhiteSpace(defaultScopes)
                ? "openid profile email"
                : defaultScopes,
            AuthorizationUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        };
    }

    /// <summary>
    /// Creates an IdentityProviderArgs for Facebook OIDC provider.
    /// </summary>
    /// <param name="alias">The alias displayed in Keycloak for this provider (e.g., "facebook").</param>
    /// <param name="clientId">The Facebook OAuth client ID.</param>
    /// <param name="clientSecret">The secret backing the Facebook OAuth client secret.</param>
    /// <param name="defaultScopes">Optional default scopes to request. Defaults to "email public_profile".</param>
    /// <param name="enabled">Whether the provider is enabled. Defaults to true.</param>
    /// <returns>IdentityProviderArgs configured for Facebook OIDC.</returns>
    public static IdentityProviderArgs CreateFacebookIdentityProviderArgs(
        string alias,
        string clientId,
        ISecret clientSecret,
        string? defaultScopes = null,
        bool enabled = true
    )
    {
        return new IdentityProviderArgs
        {
            Alias = alias,
            ProviderId = "facebook",
            Enabled = enabled,
            ClientId = clientId,
            ClientSecret = clientSecret.Value.Apply(v => v ?? string.Empty),
            DefaultScopes = string.IsNullOrWhiteSpace(defaultScopes)
                ? "email public_profile"
                : defaultScopes,
            AuthorizationUrl = "https://www.facebook.com/v10.0/dialog/oauth",
            TokenUrl = "https://graph.facebook.com/v10.0/oauth/access_token",
        };
    }

    /// <summary>
    /// Creates an IdentityProviderArgs for GitHub OIDC provider.
    /// </summary>
    /// <param name="alias">The alias displayed in Keycloak for this provider (e.g., "github").</param>
    /// <param name="clientId">The GitHub OAuth client ID.</param>
    /// <param name="clientSecret">The secret backing the GitHub OAuth client secret.</param>
    /// <param name="defaultScopes">Optional default scopes to request. Defaults to "read:user user:email".</param>
    /// <param name="enabled">Whether the provider is enabled. Defaults to true.</param>
    /// <returns>IdentityProviderArgs configured for GitHub OIDC.</returns>
    public static IdentityProviderArgs CreateGitHubIdentityProviderArgs(
        string alias,
        string clientId,
        ISecret clientSecret,
        string? defaultScopes = null,
        bool enabled = true
    )
    {
        return new IdentityProviderArgs
        {
            Alias = alias,
            ProviderId = "github",
            Enabled = enabled,
            ClientId = clientId,
            ClientSecret = clientSecret.Value.Apply(v => v ?? string.Empty),
            DefaultScopes = string.IsNullOrWhiteSpace(defaultScopes)
                ? "read:user user:email"
                : defaultScopes,
            AuthorizationUrl = "https://github.com/login/oauth/authorize",
            TokenUrl = "https://github.com/login/oauth/access_token",
        };
    }

    /// <summary>
    /// Creates an IdentityProviderArgs for LinkedIn OIDC provider.
    /// </summary>
    /// <param name="alias">The alias displayed in Keycloak for this provider (e.g., "linkedin").</param>
    /// <param name="clientId">The LinkedIn OAuth client ID.</param>
    /// <param name="clientSecret">The secret backing the LinkedIn OAuth client secret.</param>
    /// <param name="defaultScopes">Optional default scopes to request. Defaults to "r_liteprofile r_emailaddress".</param>
    /// <param name="enabled">Whether the provider is enabled. Defaults to true.</param>
    /// <returns>IdentityProviderArgs configured for LinkedIn OIDC.</returns>
    public static IdentityProviderArgs CreateLinkedInIdentityProviderArgs(
        string alias,
        string clientId,
        ISecret clientSecret,
        string? defaultScopes = null,
        bool enabled = true
    )
    {
        return new IdentityProviderArgs
        {
            Alias = alias,
            ProviderId = "linkedin",
            Enabled = enabled,
            ClientId = clientId,
            ClientSecret = clientSecret.Value.Apply(v => v ?? string.Empty),
            DefaultScopes = string.IsNullOrWhiteSpace(defaultScopes)
                ? "r_liteprofile r_emailaddress"
                : defaultScopes,
            AuthorizationUrl = "https://www.linkedin.com/oauth/v2/authorization",
            TokenUrl = "https://www.linkedin.com/oauth/v2/accessToken",
        };
    }
}
