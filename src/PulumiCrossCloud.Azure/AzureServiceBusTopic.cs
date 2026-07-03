using Pulumi;
using Pulumi.AzureNative.ServiceBus;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureServiceBusTopic : ITopicEndpoint
{
    private readonly Topic _topic;
    private readonly Output<string> _connectionString;

    public AzureServiceBusTopic(string name, string resourceGroupName, string namespaceName, PulumiCrossCloud.Core.TopicArgs? args = null, Pulumi.AzureNative.Provider? provider = null)
    {
        Name = name;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };

        _topic = new Topic(name, new Pulumi.AzureNative.ServiceBus.TopicArgs
        {
            ResourceGroupName = resourceGroupName,
            NamespaceName = namespaceName,
            TopicName = name,
            SupportOrdering = args?.Fifo ?? false,
        }, customResourceOptions);

        var rule = new TopicAuthorizationRule($"{name}-auth", new TopicAuthorizationRuleArgs
        {
            ResourceGroupName = resourceGroupName,
            NamespaceName = namespaceName,
            TopicName = _topic.Name,
            Rights = { AccessRights.Listen, AccessRights.Send },
        }, customResourceOptions);

        var invokeOptions = new InvokeOptions { Provider = provider };
        _connectionString = Output.Tuple<string, string, string>(namespaceName, _topic.Name, rule.Name).Apply(t => 
            ListTopicKeys.Invoke(new ListTopicKeysInvokeArgs
            {
                ResourceGroupName = resourceGroupName,
                NamespaceName = t.Item1,
                TopicName = t.Item2,
                AuthorizationRuleName = t.Item3
            }, invokeOptions)).Apply(keys => keys.PrimaryConnectionString);
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Topic;
    public Output<string> Arn => _topic.Id;
    public CloudProvider Provider => CloudProvider.Azure;
    public Output<string> SendEndpoint => _topic.Name;
    public Output<string> ConnectionString => _connectionString;

    public Output<bool> SupportsOrdering => _topic.SupportOrdering.Apply(x => x ?? false);

    public IEnumerable<IPolicyStatement> GetPolicy(params MessagingPermissions[] permissions)
    {
        return [];
    }
}
