using Pulumi;
using Pulumi.Gcp.PubSub;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpPubSubSubscription : IQueueEndpoint
{
    private readonly Topic? _privateTopic;
    private readonly Output<string> _topicId;
    private readonly Subscription _subscription;

    public GcpPubSubSubscription(string name, PulumiCrossCloud.Core.QueueArgs? args = null, Pulumi.Gcp.Provider? provider = null)
    {
        Name = name;
        var projectId = GcpProjectConfig.ProjectId;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };

        if (args?.SubscribeTo != null)
        {
            // Use the provided shared topic
            _topicId = args.SubscribeTo.SendEndpoint;
            _privateTopic = null;
        }
        else
        {
            // In GCP, a subscription needs a topic. To act as a standalone "Queue",
            // we create a private topic specifically for this subscription.
            _privateTopic = new Topic($"{name}-topic", new Pulumi.Gcp.PubSub.TopicArgs
            {
                Project = projectId
            }, customResourceOptions);
            _topicId = _privateTopic.Id;
        }

        _subscription = new Subscription(name, new Pulumi.Gcp.PubSub.SubscriptionArgs
        {
            Project = projectId,
            Topic = _topicId,
            AckDeadlineSeconds = args?.VisibilityTimeoutSeconds ?? 30,
        }, customResourceOptions);
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Queue;
    public Output<string> Arn => _subscription.Id;
    public CloudProvider Provider => CloudProvider.GCP;
    
    // SendEndpoint for GCP Queue is the Topic ID (where you send messages)
    public Output<string> SendEndpoint => _topicId;

    // ReceiveEndpoint for GCP Queue is the Subscription ID (where you listen)
    public Output<string> ReceiveEndpoint => _subscription.Id;

    public Output<int?> MaxDeliveryAttempts => Output.Create((int?)null);
    public Output<int?> VisibilityTimeoutSeconds => _subscription.AckDeadlineSeconds.Apply(x => (int?)x);
    public Output<string?> DeadLetterTarget => Output.Create((string?)null);

    public IEnumerable<IPolicyStatement> GetPolicy(params MessagingPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case MessagingPermissions.Send:
                    // Permissions to send to the underlying topic
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["roles/pubsub.publisher"],
                        Resources = [_topicId],
                        ResourceNames = [Name]
                    });
                    break;
                case MessagingPermissions.Receive:
                    // Permissions to consume from the subscription
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["roles/pubsub.subscriber"],
                        Resources = [Arn],
                        ResourceNames = [Name]
                    });
                    break;
                case MessagingPermissions.Admin:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["roles/pubsub.admin"],
                        Resources = [Arn, _topicId],
                        ResourceNames = [Name, Name]
                    });
                    break;
            }
        }
        return statements;
    }
}
