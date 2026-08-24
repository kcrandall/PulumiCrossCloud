using Pulumi;
using Pulumi.Cloudflare;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Cloudflare;

public class CloudflareR2Credentials : ComponentResource
{
    internal const string ObjectReadWritePermissionGroupId = "2efd5506f9c8494dacb1fa10a3e7d5b6";
    internal const string LegacyWorkersR2StorageWritePermissionGroupId =
        "bf7481a1826f439697cb59a20b22293e";

    private readonly AccountApiToken _token;

    public Output<string> AccessKeyId => _token.TokenId;
    public Output<string> SecretAccessKey { get; }

    /// <summary>
    /// Creates S3-compatible credentials restricted to one R2 bucket.
    /// </summary>
    public CloudflareR2Credentials(
        string name,
        string accountId,
        string bucketName,
        Input<string> primaryToken,
        Provider? provider = null
    )
        : this(name, accountId, primaryToken, provider, bucketName) { }

    /// <summary>
    /// Creates the historical account-scoped R2 token. Prefer the bucket-name overload so the
    /// generated S3 credentials cannot access other buckets in the account.
    /// </summary>
    [Obsolete(
        "Use the overload that accepts bucketName to create bucket-scoped R2 S3 credentials."
    )]
    public CloudflareR2Credentials(
        string name,
        string accountId,
        Input<string> primaryToken,
        Provider? provider = null
    )
        : this(name, accountId, primaryToken, provider, null) { }

    private CloudflareR2Credentials(
        string name,
        string accountId,
        Input<string> primaryToken,
        Provider? provider,
        string? bucketName
    )
        : base(
            "crosscloud:cloudflare:CloudflareR2Credentials",
            name,
            new ComponentResourceOptions { Provider = provider }
        )
    {
        string permissionGroupId;
        IReadOnlyDictionary<string, string> resources;

        if (bucketName is null)
        {
            permissionGroupId = LegacyWorkersR2StorageWritePermissionGroupId;
            resources = new Dictionary<string, string>
            {
                [$"com.cloudflare.api.account.{accountId}"] = "*",
            };
        }
        else
        {
            var policy = CreateBucketPolicy(accountId, bucketName);
            permissionGroupId = policy.PermissionGroupId;
            resources = new Dictionary<string, string> { [policy.Resource] = "*" };
        }

        _token = new AccountApiToken(
            name,
            accountId,
            primaryToken,
            [permissionGroupId],
            resources,
            new ComponentResourceOptions { Parent = this }
        );

        // Compute the Secret Access Key (SHA-256 hash of the token value)
        SecretAccessKey = _token.TokenValue.Apply(value =>
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            var hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        });
    }

    internal static (string PermissionGroupId, string Resource) CreateBucketPolicy(
        string accountId,
        string bucketName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);

        return (
            ObjectReadWritePermissionGroupId,
            $"com.cloudflare.edge.r2.bucket.{accountId}_default_{bucketName}"
        );
    }
}
