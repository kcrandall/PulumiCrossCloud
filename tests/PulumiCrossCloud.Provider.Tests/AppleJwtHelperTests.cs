using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PulumiCrossCloud.Keycloak;

namespace PulumiCrossCloud.Provider.Tests;

public sealed class AppleJwtHelperTests
{
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\r\n", false)]
    public void StandardPemFormatting_ProducesValidAppleClientSecrets(
        string newline,
        bool trailingNewline
    )
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem().Replace("\n", newline);
        if (trailingNewline)
            pem += newline;

        // Repeated calls also catch cached signing providers holding disposed ECDsa keys.
        for (var i = 0; i < 2; i++)
        {
            var jwt = AppleJwtHelper.GenerateAppleClientSecretJwt(
                "test-client",
                "test-team",
                "test-key",
                pem
            );
            var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
            Assert.Equal("test-client", token.Subject);
            Assert.Equal("test-team", token.Issuer);
            Assert.Equal("test-key", token.Header.Kid);
            Assert.Contains("https://appleid.apple.com", token.Audiences);

            var parts = jwt.Split('.');
            Assert.True(
                key.VerifyData(
                    Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
                    Base64UrlEncoder.DecodeBytes(parts[2]),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation
                )
            );
        }
    }
}
