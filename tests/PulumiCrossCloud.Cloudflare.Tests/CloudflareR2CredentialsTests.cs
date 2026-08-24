using System.Text.Json;

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
    public void TokenPayload_PreservesTheBucketResourceWithoutAddingAccountWideAccess()
    {
        var policy = CloudflareR2Credentials.CreateBucketPolicy(AccountId, BucketName);
        var payload = AccountApiToken.SerializePayload(
            "nightfall-assets-creds",
            [policy.PermissionGroupId],
            new Dictionary<string, string> { [policy.Resource] = "*" }
        );

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var tokenPolicy = root.GetProperty("policies").EnumerateArray().Single();
        var resources = tokenPolicy.GetProperty("resources").EnumerateObject().ToArray();
        var permissionGroups = tokenPolicy
            .GetProperty("permission_groups")
            .EnumerateArray()
            .ToArray();

        Assert.Equal("nightfall-assets-creds", root.GetProperty("name").GetString());
        Assert.Equal("allow", tokenPolicy.GetProperty("effect").GetString());
        Assert.Single(resources);
        Assert.Equal(policy.Resource, resources[0].Name);
        Assert.Equal("*", resources[0].Value.GetString());
        Assert.DoesNotContain(
            resources,
            resource =>
                resource.Name.StartsWith("com.cloudflare.api.account.", StringComparison.Ordinal)
        );
        Assert.Single(permissionGroups);
        Assert.Equal(policy.PermissionGroupId, permissionGroups[0].GetProperty("id").GetString());
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
