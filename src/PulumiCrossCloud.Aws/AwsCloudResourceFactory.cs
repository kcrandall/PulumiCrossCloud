using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsCloudResourceFactory : CloudResourceFactory
{
    private readonly Pulumi.Aws.Provider? _awsProvider;

    public AwsCloudResourceFactory(Pulumi.Aws.Provider? provider = null)
        : base(CloudProvider.AWS)
    {
        _awsProvider = provider;
    }

    public override ISecret CreateSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false
    )
    {
        return new AwsSecret(name, value, ignoreChangesAfterCreate, _awsProvider);
    }

    public override IContainerRegistry CreateContainerRegistry(
        string name,
        bool createDefaultImage = true
    )
    {
        return new AwsContainerRegistry(name, createDefaultImage, _awsProvider);
    }

    public override IContainerImage CreateContainerImage(string name, IContainerRegistry registry)
    {
        return new AwsContainerImage(name, registry);
    }

    public override ICompute CreateCompute(
        string name,
        IContainerRegistry registry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        ComputeServiceType? serviceType = null
    )
    {
        if (serviceType is null)
            serviceType = ComputeServiceType.AppRunner;

        return serviceType switch
        {
            ComputeServiceType.AppRunner => new AwsAppRunnerCompute(
                name,
                registry,
                imageName,
                imageTag,
                containerImage,
                _awsProvider
            ),
            _ => throw new ArgumentException($"Unsupported AWS service type: {serviceType}"),
        };
    }

    public override IRole CreateRole(string name, string assumeRolePolicy)
    {
        return new AwsRole(name, assumeRolePolicy, _awsProvider);
    }

    public override IBucket CreateBucket(
        string name,
        bool isPublic = false,
        BucketCdnConfiguration? cdn = null,
        bool versioningEnabled = false,
        IEnumerable<CorsRule>? corsRules = null,
        string? indexDocument = null
    )
    {
        return new AwsBucket(
            name,
            isPublic,
            cdn,
            versioningEnabled,
            corsRules,
            indexDocument,
            _awsProvider
        );
    }

    public override IDnsZone CreateDnsZone(string name, string domainName)
    {
        return new AwsDnsZone(name, domainName, _awsProvider);
    }

    public override IDnsZone LookupDnsZone(string name, string zoneName)
    {
        return AwsDnsZone.Lookup(name, zoneName, _awsProvider);
    }

    public override IPostgresDatabase CreatePostgresDatabase(
        string name,
        IPostgresOptions? options = null
    )
    {
        return new AwsPostgresDatabase(name, options, _awsProvider);
    }

    public override IQueueEndpoint CreateQueue(string name, QueueArgs? args = null)
    {
        return new AwsSqsQueue(name, args, _awsProvider);
    }

    public override ITopicEndpoint CreateTopic(string name, TopicArgs? args = null)
    {
        return new AwsSnsTopic(name, args, _awsProvider);
    }

    public override IStreamEndpoint CreateStream(string name, StreamArgs? args = null)
    {
        return new AwsKinesisStream(name, args, _awsProvider);
    }

    public override ISearchDatabase CreateSearchDatabase(
        string name,
        SearchDatabaseType type,
        ISearchDatabaseOptions? options = null
    )
    {
        return new AwsSearchDatabase(name, type, options, _awsProvider);
    }
}
