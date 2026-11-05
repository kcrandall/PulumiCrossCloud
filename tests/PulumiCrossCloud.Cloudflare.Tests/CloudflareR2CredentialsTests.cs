using Pulumi;
using Pulumi.Testing;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PulumiCrossCloud.Cloudflare.Tests;

public sealed class CloudflareR2CredentialsTests
{
    private const string AccountId = "0123456789abcdef0123456789abcdef";
    private const string BucketName = "nightfall-assets";

    [Fact]
    public void BucketPolicy_UsesObjectReadWritePermissionForOnlyTheRequestedBucket()
    {
        var policy = CloudflareR2Credentials.CreateBucketPolicy(AccountId, BucketName);

        Assert.Equal("2efd5506f9c8494dacb1fa10a3e7d5b6", policy.PermissionGroupId);
        Assert.Equal(
            $"com.cloudflare.edge.r2.bucket.{AccountId}_default_{BucketName}",
            policy.Resource
        );
    }

    [Fact]
    public async Task Token_IsScopedToTheBucketWithoutAccountWideAccess()
    {
        var deployment = await R2Deployment.RunAsync();
        var token = deployment.Resources.Single(r => r.Type == CloudflareAccountToken.TypeToken);
        var policy = CloudflareR2Credentials.CreateBucketPolicy(AccountId, BucketName);

        Assert.Equal("nightfall-assets-creds-token", token.Name);
        Assert.Equal("nightfall-assets-creds", token.Inputs["name"]);
        Assert.Equal(AccountId, token.Inputs["accountId"]);
        Assert.Equal(
            [policy.PermissionGroupId],
            ((IEnumerable<object>)token.Inputs["permissionGroupIds"]).Cast<string>()
        );
        var resources = (IDictionary<string, object>)token.Inputs["resources"];
        Assert.Equal("*", Assert.Single(resources, pair => pair.Key == policy.Resource).Value);
        Assert.Single(resources);
    }

    [Fact]
    public async Task Credentials_DeriveTheS3KeysFromTheTokenAndKeepThemSecret()
    {
        var deployment = await R2Deployment.RunAsync();

        Assert.Equal("nightfall-assets-creds-token-id", deployment.AccessKeyId);
        Assert.Equal(
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData("test-token-value"u8.ToArray())
            ),
            deployment.SecretAccessKey
        );
        Assert.True(deployment.SecretAccessKeyIsSecret);
        Assert.True(deployment.TokenValueIsSecret);
    }

    [Fact]
    public async Task Token_UsesAnExplicitProviderWithThePrimaryTokenAsASecret()
    {
        var deployment = await R2Deployment.RunAsync();
        var provider = deployment.Resources.Single(r =>
            r.Type == "pulumi:providers:crosscloud-cloudflare"
        );

        Assert.Equal("nightfall-assets-creds-provider", provider.Name);
        Assert.Equal("test-primary-token", provider.Inputs["apiToken"]);
        Assert.DoesNotContain(deployment.Resources, r => r.Type?.StartsWith("command:") == true);
    }

    [Theory]
    [InlineData("", BucketName)]
    [InlineData(AccountId, "")]
    public void BucketPolicy_RejectsMissingScopeParts(string accountId, string bucketName)
    {
        Assert.Throws<ArgumentException>(() =>
            CloudflareR2Credentials.CreateBucketPolicy(accountId, bucketName)
        );
    }
}

internal sealed record R2Deployment(
    IReadOnlyList<MockResourceArgs> Resources,
    string AccessKeyId,
    string SecretAccessKey,
    bool SecretAccessKeyIsSecret,
    bool TokenValueIsSecret
)
{
    public static async Task<R2Deployment> RunAsync()
    {
        var mocks = new R2CredentialsMocks();
        CloudflareR2Credentials? credentials = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                credentials = new CloudflareR2Credentials(
                    "nightfall-assets-creds",
                    "0123456789abcdef0123456789abcdef",
                    "nightfall-assets",
                    "test-primary-token"
                );
            }
        );

        var (accessKeyId, _) = await Values(credentials!.AccessKeyId);
        var (secretAccessKey, secret) = await Values(credentials.SecretAccessKey);
        var token = (AccountApiToken)
            typeof(CloudflareR2Credentials)
                .GetField("_token", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(credentials)!;
        var (_, tokenSecret) = await Values(token.TokenValue);
        return new R2Deployment([.. mocks.Resources], accessKeyId, secretAccessKey, secret, tokenSecret);
    }

    private static async Task<(string Value, bool Secret)> Values(Output<string> output)
    {
        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        output.Apply(value =>
        {
            completion.TrySetResult(value);
            return value;
        });
        return (await completion.Task, await Output.IsSecretAsync(output));
    }
}

public sealed class R2CredentialsMocks : IMocks
{
    public System.Collections.Concurrent.ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

    public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
    {
        Resources.Enqueue(args);
        var name = args.Name ?? "resource";
        var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (args.Type == CloudflareAccountToken.TypeToken)
        {
            state["value"] = "test-token-value";
            state["status"] = "active";
        }

        return Task.FromResult<(string? id, object state)>(($"{name}-id", state));
    }

    public Task<object> CallAsync(MockCallArgs args) =>
        Task.FromResult<object>(new Dictionary<string, object>());
}
