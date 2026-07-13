using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
        var securityKey = GetAppleSecurityKey(privateKey, keyId);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(
            issuer: teamId,
            audience: "https://appleid.apple.com",
            subject: new ClaimsIdentity(new[] { new Claim("sub", clientId) }),
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(expirationMinutes).UtcDateTime,
            issuedAt: now.UtcDateTime,
            signingCredentials: credentials
        );
        token.Header["kid"] = keyId;
        return handler.WriteToken(token);
    }

    private static ECDsaSecurityKey GetAppleSecurityKey(string privateKeyPem, string keyId)
    {
        // Remove PEM header/footer and decode base64
        var keyLines = privateKeyPem.Split('\n');
        var base64 = string.Join("", keyLines[1..^1]);
        var keyBytes = Convert.FromBase64String(base64);
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(keyBytes, out _);
        var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        return securityKey;
    }
}
