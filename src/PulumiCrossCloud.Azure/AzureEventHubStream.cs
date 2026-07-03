using Pulumi;
using Pulumi.AzureNative.EventHub;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureEventHubStream : IStreamEndpoint
{
    private readonly EventHub _hub;
    private readonly Output<string> _connectionString;

    public AzureEventHubStream(string name, string resourceGroupName, string namespaceName, PulumiCrossCloud.Core.StreamArgs? args = null, Pulumi.AzureNative.Provider? provider = null)
    {
        Name = name;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };

        _hub = new EventHub(name, new Pulumi.AzureNative.EventHub.EventHubArgs
        {
            ResourceGroupName = resourceGroupName,
            NamespaceName = namespaceName,
            EventHubName = name,
            MessageRetentionInDays = (args?.RetentionHours ?? 24) / 24,
            PartitionCount = args?.ThroughputUnits ?? 2,
        }, customResourceOptions);

        var rule = new EventHubAuthorizationRule($"{name}-auth", new EventHubAuthorizationRuleArgs
        {
            ResourceGroupName = resourceGroupName,
            NamespaceName = namespaceName,
            EventHubName = _hub.Name,
            Rights = { AccessRights.Listen, AccessRights.Send },
        }, customResourceOptions);

        var invokeOptions = new InvokeOptions { Provider = provider };
        _connectionString = Output.Tuple<string, string, string>(namespaceName, _hub.Name, rule.Name).Apply(t => 
            ListEventHubKeys.Invoke(new ListEventHubKeysInvokeArgs
            {
                ResourceGroupName = resourceGroupName,
                NamespaceName = t.Item1,
                EventHubName = t.Item2,
                AuthorizationRuleName = t.Item3
            }, invokeOptions)).Apply(keys => keys.PrimaryConnectionString);
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Stream;
    public Output<string> Arn => _hub.Id;
    public CloudProvider Provider => CloudProvider.Azure;
    public Output<string> SendEndpoint => _hub.Name;
    public Output<string> ReceiveEndpoint => _hub.Name;
    public Output<string> ConnectionString => _connectionString;

    public Output<int?> RetentionHours => _hub.MessageRetentionInDays.Apply(x => (int?)(x * 24));
    public Output<int?> ThroughputUnits => _hub.PartitionCount.Apply(x => (int?)x);

    public IEnumerable<IPolicyStatement> GetPolicy(params MessagingPermissions[] permissions)
    {
        return [];
    }
}
