using Pulumi;
using Pulumi.Gcp.PubSub;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpPubSubStream : IStreamEndpoint
{
    private readonly Topic _topic;

    public GcpPubSubStream(string name, PulumiCrossCloud.Core.StreamArgs? args = null, Pulumi.Gcp.Provider? provider = null)
    {
        Name = name;
        
        // Pub/Sub Topics can have message retention
        _topic = new Topic(name, new Pulumi.Gcp.PubSub.TopicArgs
        {
            Project = GcpProjectConfig.ProjectId,
            MessageRetentionDuration = args?.RetentionHours != null ? $"{args.RetentionHours * 3600}s" : "86400s"
        }, new CustomResourceOptions { Provider = provider });
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Stream;
    public Output<string> Arn => _topic.Id;
    public CloudProvider Provider => CloudProvider.GCP;
    public Output<string> SendEndpoint => _topic.Id;
    public Output<string> ReceiveEndpoint => _topic.Id;

    public Output<int?> RetentionHours => Output.Create((int?)null); // Extract from Duration if needed
    public Output<int?> ThroughputUnits => Output.Create((int?)null); // Pub/Sub is auto-scaling

    public IEnumerable<IPolicyStatement> GetPolicy(params MessagingPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case MessagingPermissions.Send:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["roles/pubsub.publisher"],
                        Resources = [Arn]
                    });
                    break;
                case MessagingPermissions.Admin:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["roles/pubsub.admin"],
                        Resources = [Arn]
                    });
                    break;
            }
        }
        return statements;
    }
}
