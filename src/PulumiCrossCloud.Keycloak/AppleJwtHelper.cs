using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace PulumiCrossCloud.Keycloak;

/// <summary>
/// Helper for generating Apple client secret JWT for Sign In with Apple OIDC.
/// </summary>
public static class AppleJwtHelper
{
    /// <summary>
    /// Generates a JWT for Apple client secret using the provided credentials and private key.
    /// </summary>
    /// <param name="clientId">Apple Services ID (client_id).</param>
    /// <param name="teamId">Apple Developer Team ID (iss).</param>
    /// <param name="keyId">Apple Key ID (kid).</param>
    /// <param name="privateKey">Apple private key (.p8 PEM format).</param>
    /// <param name="expirationMinutes">Expiration in minutes (default: 10).</param>
    /// <returns>Signed JWT string for Apple client secret.</returns>
    public static string GenerateAppleClientSecretJwt(
        string clientId,
        string teamId,
        string keyId,
        string privateKey,
        int expirationMinutes = 10
    )
    {
        var now = DateTimeOffset.UtcNow;
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKey);
        var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(
            issuer: teamId,
            audience: "https://appleid.apple.com",
            subject: new ClaimsIdentity([new Claim("sub", clientId)]),
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(expirationMinutes).UtcDateTime,
            issuedAt: now.UtcDateTime,
            signingCredentials: credentials
        );
        token.Header["kid"] = keyId;
        return handler.WriteToken(token);
    }
}
