using Pulumi;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureBlobBucket : IBucket
{
    private readonly StorageAccount _storageAccount;
    private readonly BlobContainer _container;

    public AzureBlobBucket(
        string name,
        string resourceGroupName,
        bool isPublic = false,
        BucketCdnConfiguration? cdn = null,
        bool versioningEnabled = false,
        IEnumerable<CorsRule>? corsRules = null,
        string? indexDocument = null,
        Pulumi.AzureNative.Provider? provider = null
    )
    {
        Name = name;
        IsPublic = isPublic;
        Cdn = cdn;
        VersioningEnabled = versioningEnabled;
        IndexDocument = indexDocument;
        CorsRules = corsRules ?? [];

        var customResourceOptions = new CustomResourceOptions { Provider = provider };

        var accountName = name.Replace("-", "");
        accountName = accountName[..Math.Min(24, accountName.Length)];

        _storageAccount = new StorageAccount(
            name.Replace("-", ""),
            new StorageAccountArgs
            {
                ResourceGroupName = resourceGroupName,
                AccountName = accountName,
                Kind = Kind.StorageV2,
                Sku = new SkuArgs { Name = SkuName.Standard_LRS },
            },
            customResourceOptions
        );

        _container = new BlobContainer(
            name,
            new BlobContainerArgs
            {
                ResourceGroupName = resourceGroupName,
                AccountName = _storageAccount.Name,
                ContainerName = name,
                PublicAccess = isPublic ? PublicAccess.Blob : PublicAccess.None,
            },
            customResourceOptions
        );

        if (versioningEnabled)
        {
            // Versioning is managed at the blob service level in Azure
            new BlobServiceProperties(
                $"{name}-blobservice",
                new BlobServicePropertiesArgs
                {
                    ResourceGroupName = resourceGroupName,
                    AccountName = _storageAccount.Name,
                    BlobServicesName = "default",
                    IsVersioningEnabled = true,
                },
                customResourceOptions
            );
        }

        if (!string.IsNullOrEmpty(IndexDocument))
            new StorageAccountStaticWebsite(
                $"{name}-static-website",
                new StorageAccountStaticWebsiteArgs
                {
                    ResourceGroupName = resourceGroupName,
                    AccountName = _storageAccount.Name,
                    IndexDocument = IndexDocument,
                    Error404Document = "404.html",
                },
                customResourceOptions
            );
    }

    public string Name { get; }
    public CloudProvider Provider => CloudProvider.Azure;
    public Output<string> Arn => _container.Id;
    public bool IsPublic { get; }
    public BucketCdnConfiguration? Cdn { get; }
    public bool VersioningEnabled { get; }
    public string? IndexDocument { get; }
    public IEnumerable<CorsRule> CorsRules { get; }
    public Resource Resource => _container;

    public BucketDnsRecords GetRequiredDnsRecords(IEnumerable<string> hostNames)
    {
        return new BucketDnsRecords([], []);
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params BucketPermissions[] permissions)
    {
        return [];
    }

    public Output<string> SyncDirectory(
        string localPath,
        string? keyPrefix = null,
        BucketSyncOptions? options = null
    )
    {
        var deployment = new AzureBucketDeployment(
            $"{Name}-deploy",
            _storageAccount.Name,
            Name,
            localPath,
            keyPrefix,
            options,
            new ComponentResourceOptions { Parent = _container }
        );
        return deployment.WebsiteUrl;
    }
}
