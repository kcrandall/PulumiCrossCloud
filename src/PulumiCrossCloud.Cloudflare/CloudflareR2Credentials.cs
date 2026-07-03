using Pulumi;
using Pulumi.Cloudflare;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Cloudflare;

public class CloudflareR2Credentials : ComponentResource
{
    private readonly AccountApiToken _token;

    public Output<string> AccessKeyId => _token.TokenId;
    public Output<string> SecretAccessKey { get; }

    public CloudflareR2Credentials(string name, string accountId, Input<string> primaryToken, Provider? provider = null)
        : base("crosscloud:cloudflare:CloudflareR2Credentials", name, new ComponentResourceOptions { Provider = provider })
    {
        // R2 Permission Group IDs (using "Workers R2 Storage Write" as it's the only one available to the parent token)
        var writeId = "bf7481a1826f439697cb59a20b22293e";

        _token = new AccountApiToken(name, accountId, primaryToken, [writeId], new ComponentResourceOptions { Parent = this });

        // Compute the Secret Access Key (SHA-256 hash of the token value)
        SecretAccessKey = _token.TokenValue.Apply(value => 
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            var hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        });
    }
}
