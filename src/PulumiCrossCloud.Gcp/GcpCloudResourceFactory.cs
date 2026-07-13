using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpCloudResourceFactory : CloudResourceFactory
{
    private readonly Pulumi.Gcp.Provider? _gcpProvider;

    public GcpCloudResourceFactory(Pulumi.Gcp.Provider? provider = null)
        : base(CloudProvider.GCP)
    {
        _gcpProvider = provider;
    }

    public override ISecret CreateSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false
    )
    {
        return new GcpSecret(name, value, ignoreChangesAfterCreate, _gcpProvider);
    }

    public override ISecret CreateSecret(
        string name,
        Output<string> value,
        bool ignoreChangesAfterCreate = false
    )
    {
        return new GcpSecret(name, value, ignoreChangesAfterCreate, _gcpProvider);
    }

    public override IContainerRegistry CreateContainerRegistry(
        string name,
        bool createDefaultImage = false
    )
    {
        return new GcpContainerRegistry(name, createDefaultImage, _gcpProvider);
    }

    public override IContainerImage CreateContainerImage(string name, IContainerRegistry registry)
    {
        return new GcpContainerImage(name, registry);
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
            serviceType = ComputeServiceType.CloudRun;

        return serviceType switch
        {
            ComputeServiceType.CloudRun => new GcpCloudRunCompute(
                name,
                registry,
                imageName,
                imageTag,
                containerImage,
                null,
                _gcpProvider
            ),
            _ => throw new ArgumentException($"Unsupported GCP service type: {serviceType}"),
        };
    }

    public override IRole CreateRole(string name, string assumeRolePolicy)
    {
        // GCP doesn't have assume role policies like AWS
        // Create a service account instead, which is the GCP equivalent
        return new GcpServiceAccount(name, provider: _gcpProvider);
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
        return new GcpBucket(
            name,
            isPublic,
            cdn,
            versioningEnabled,
            corsRules,
            indexDocument,
            _gcpProvider
        );
    }

    public override IDnsZone CreateDnsZone(string name, string domainName)
    {
        return new GcpDnsZone(name, domainName, _gcpProvider);
    }

    public override IDnsZone LookupDnsZone(string name, string zoneName)
    {
        return GcpDnsZone.Lookup(name, zoneName, _gcpProvider);
    }

    public override IPostgresDatabase CreatePostgresDatabase(
        string name,
        IPostgresOptions? options = null
    )
    {
        return new GcpPostgresDatabase(name, options, _gcpProvider);
    }

    public override IQueueEndpoint CreateQueue(string name, QueueArgs? args = null)
    {
        return new GcpPubSubSubscription(name, args);
    }

    public override ITopicEndpoint CreateTopic(string name, TopicArgs? args = null)
    {
        return new GcpPubSubTopic(name, args);
    }

    public override IStreamEndpoint CreateStream(string name, StreamArgs? args = null)
    {
        return new GcpPubSubStream(name, args);
    }
}
