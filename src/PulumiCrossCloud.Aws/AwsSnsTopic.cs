using Pulumi;
using Pulumi.Aws.Sns;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsSnsTopic : ITopicEndpoint
{
    private readonly Topic _topic;

    public AwsSnsTopic(
        string name,
        PulumiCrossCloud.Core.TopicArgs? args = null,
        Pulumi.Aws.Provider? provider = null
    )
    {
        Name = name;
        // The provider appends ".fifo" itself when it autonames a FIFO resource, so a suffix
        // in the logical name would end up mid-name and fail AWS name validation.
        var resourceName =
            args?.Fifo == true && name.EndsWith(".fifo") ? name[..^".fifo".Length] : name;
        _topic = new Topic(
            resourceName,
            new Pulumi.Aws.Sns.TopicArgs
            {
                FifoTopic = args?.Fifo ?? false,
                ContentBasedDeduplication = args?.ContentBasedDeduplication ?? false,
            },
            new CustomResourceOptions { Provider = provider }
        );
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Topic;
    public Output<string> Arn => _topic.Arn;
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> SendEndpoint => _topic.Arn;

    public Output<bool> SupportsOrdering => _topic.FifoTopic.Apply(x => x ?? false);

    public IEnumerable<IPolicyStatement> GetPolicy(params MessagingPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
            switch (permission)
            {
                case MessagingPermissions.Send:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = ["sns:Publish"],
                            Resources = [Arn],
                        }
                    );
                    break;
                case MessagingPermissions.Receive:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = ["sns:Subscribe", "sns:Receive"],
                            Resources = [Arn],
                        }
                    );
                    break;
                case MessagingPermissions.Admin:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = ["sns:*"],
                            Resources = [Arn],
                        }
                    );
                    break;
            }

        return statements;
    }
}
