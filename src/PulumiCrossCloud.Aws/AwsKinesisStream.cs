using Pulumi;
using Pulumi.Aws.Kinesis;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsKinesisStream : IStreamEndpoint
{
    private readonly Pulumi.Aws.Kinesis.Stream _stream;

    public AwsKinesisStream(
        string name,
        PulumiCrossCloud.Core.StreamArgs? args = null,
        Pulumi.Aws.Provider? provider = null
    )
    {
        Name = name;
        _stream = new Pulumi.Aws.Kinesis.Stream(
            name,
            new Pulumi.Aws.Kinesis.StreamArgs
            {
                ShardCount = args?.ThroughputUnits ?? 1,
                RetentionPeriod = args?.RetentionHours ?? 24,
            },
            new CustomResourceOptions { Provider = provider }
        );
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Stream;
    public Output<string> Arn => _stream.Arn;
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> SendEndpoint => _stream.Name;
    public Output<string> ReceiveEndpoint => _stream.Name;

    public Output<int?> RetentionHours => _stream.RetentionPeriod;
    public Output<int?> ThroughputUnits => _stream.ShardCount;

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
                            Actions = ["kinesis:PutRecord", "kinesis:PutRecords"],
                            Resources = [Arn],
                        }
                    );
                    break;
                case MessagingPermissions.Receive:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions =
                            [
                                "kinesis:GetRecords",
                                "kinesis:GetShardIterator",
                                "kinesis:DescribeStream",
                            ],
                            Resources = [Arn],
                        }
                    );
                    break;
                case MessagingPermissions.Admin:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = ["kinesis:*"],
                            Resources = [Arn],
                        }
                    );
                    break;
            }

        return statements;
    }
}
