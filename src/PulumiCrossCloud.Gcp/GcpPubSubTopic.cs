using Pulumi;
using Pulumi.Gcp.PubSub;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpPubSubTopic : ITopicEndpoint
{
    private readonly Topic _topic;

    public GcpPubSubTopic(
        string name,
        PulumiCrossCloud.Core.TopicArgs? args = null,
        Pulumi.Gcp.Provider? provider = null
    )
    {
        Name = name;
        _topic = new Topic(
            name,
            new Pulumi.Gcp.PubSub.TopicArgs { Project = GcpProjectConfig.ProjectId },
            new CustomResourceOptions { Provider = provider }
        );
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Topic;
    public Output<string> Arn => _topic.Id; // projects/{project}/topics/{topic}
    public CloudProvider Provider => CloudProvider.GCP;
    public Output<string> SendEndpoint => _topic.Id;

    public Output<bool> SupportsOrdering => Output.Create(true); // GCP topics support ordering keys

    public IEnumerable<IPolicyStatement> GetPolicy(params MessagingPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
            switch (permission)
            {
                case MessagingPermissions.Send:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = ["roles/pubsub.publisher"],
                            Resources = [Arn],
                        }
                    );
                    break;
                case MessagingPermissions.Admin:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = ["roles/pubsub.admin"],
                            Resources = [Arn],
                        }
                    );
                    break;
            }

        return statements;
    }
}
