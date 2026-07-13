using Pulumi;
using Pulumi.AzureNative.ServiceBus;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureServiceBusQueue : IQueueEndpoint
{
    private readonly Queue _queue;
    private readonly Output<string> _connectionString;

    public AzureServiceBusQueue(
        string name,
        string resourceGroupName,
        string namespaceName,
        PulumiCrossCloud.Core.QueueArgs? args = null,
        Pulumi.AzureNative.Provider? provider = null
    )
    {
        Name = name;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };

        _queue = new Queue(
            name,
            new Pulumi.AzureNative.ServiceBus.QueueArgs
            {
                ResourceGroupName = resourceGroupName,
                NamespaceName = namespaceName,
                QueueName = name,
                MaxDeliveryCount = args?.MaxDeliveryAttempts ?? 10,
                LockDuration =
                    args?.VisibilityTimeoutSeconds != null
                        ? $"PT{args.VisibilityTimeoutSeconds}S"
                        : "PT30S",
            },
            customResourceOptions
        );

        var rule = new QueueAuthorizationRule(
            $"{name}-auth",
            new QueueAuthorizationRuleArgs
            {
                ResourceGroupName = resourceGroupName,
                NamespaceName = namespaceName,
                QueueName = _queue.Name,
                Rights = { AccessRights.Listen, AccessRights.Send },
            },
            customResourceOptions
        );

        var invokeOptions = new InvokeOptions { Provider = provider };
        _connectionString = Output
            .Tuple<string, string, string>(namespaceName, _queue.Name, rule.Name)
            .Apply(t =>
                ListQueueKeys.Invoke(
                    new ListQueueKeysInvokeArgs
                    {
                        ResourceGroupName = resourceGroupName,
                        NamespaceName = t.Item1,
                        QueueName = t.Item2,
                        AuthorizationRuleName = t.Item3,
                    },
                    invokeOptions
                )
            )
            .Apply(keys => keys.PrimaryConnectionString);
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Queue;
    public Output<string> Arn => _queue.Id;
    public CloudProvider Provider => CloudProvider.Azure;
    public Output<string> SendEndpoint => _queue.Name;
    public Output<string> ReceiveEndpoint => _queue.Name;
    public Output<string> ConnectionString => _connectionString;

    public Output<int?> MaxDeliveryAttempts => _queue.MaxDeliveryCount.Apply(x => (int?)x);
    public Output<int?> VisibilityTimeoutSeconds => Output.Create((int?)null); // Parsing TimeSpan string is complex
    public Output<string?> DeadLetterTarget => Output.Create((string?)null);

    public IEnumerable<IPolicyStatement> GetPolicy(params MessagingPermissions[] permissions)
    {
        return [];
    }
}
