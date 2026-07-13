using Pulumi;
using Pulumi.AzureNative;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureCloudResourceFactory : CloudResourceFactory
{
    private readonly string? _resourceGroupName;
    private readonly string? _serviceBusNamespace;
    private readonly string? _eventHubNamespace;
    private readonly Provider? _azureProvider;

    public AzureCloudResourceFactory(
        string? resourceGroupName = null,
        string? serviceBusNamespace = null,
        string? eventHubNamespace = null,
        Provider? provider = null
    )
        : base(CloudProvider.Azure)
    {
        _resourceGroupName = resourceGroupName;
        _serviceBusNamespace = serviceBusNamespace;
        _eventHubNamespace = eventHubNamespace;
        _azureProvider = provider;
    }

    private string EnsureResourceGroup() =>
        _resourceGroupName
        ?? (
            string.IsNullOrEmpty(AzureProjectConfig.ResourceGroupName)
                ? throw new InvalidOperationException(
                    "Resource Group Name must be configured for Azure factory."
                )
                : AzureProjectConfig.ResourceGroupName
        );

    public override IPostgresDatabase CreatePostgresDatabase(
        string name,
        IPostgresOptions? options = null
    )
    {
        return new AzurePostgresDatabase(name, EnsureResourceGroup(), options, _azureProvider);
    }

    public override IQueueEndpoint CreateQueue(string name, QueueArgs? args = null)
    {
        var ns =
            _serviceBusNamespace
            ?? (
                string.IsNullOrEmpty(AzureProjectConfig.ServiceBusNamespace)
                    ? throw new InvalidOperationException(
                        "Service Bus Namespace must be configured."
                    )
                    : AzureProjectConfig.ServiceBusNamespace
            );
        return new AzureServiceBusQueue(name, EnsureResourceGroup(), ns, args, _azureProvider);
    }

    public override ITopicEndpoint CreateTopic(string name, TopicArgs? args = null)
    {
        var ns =
            _serviceBusNamespace
            ?? (
                string.IsNullOrEmpty(AzureProjectConfig.ServiceBusNamespace)
                    ? throw new InvalidOperationException(
                        "Service Bus Namespace must be configured."
                    )
                    : AzureProjectConfig.ServiceBusNamespace
            );
        return new AzureServiceBusTopic(name, EnsureResourceGroup(), ns, args, _azureProvider);
    }

    public override IStreamEndpoint CreateStream(string name, StreamArgs? args = null)
    {
        var ns =
            _eventHubNamespace
            ?? (
                string.IsNullOrEmpty(AzureProjectConfig.EventHubNamespace)
                    ? throw new InvalidOperationException("Event Hub Namespace must be configured.")
                    : AzureProjectConfig.EventHubNamespace
            );
        return new AzureEventHubStream(name, EnsureResourceGroup(), ns, args, _azureProvider);
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
        return new AzureBlobBucket(
            name,
            EnsureResourceGroup(),
            isPublic,
            cdn,
            versioningEnabled,
            corsRules,
            indexDocument,
            _azureProvider
        );
    }
}
