using Pulumi;
using Pulumi.Aws.Sqs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsSqsQueue : IQueueEndpoint
{
    private readonly Queue _queue;
    private readonly Pulumi.Aws.Sns.TopicSubscription? _snsSubscription;

    public AwsSqsQueue(
        string name,
        PulumiCrossCloud.Core.QueueArgs? args = null,
        Pulumi.Aws.Provider? provider = null
    )
    {
        Name = name;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };
        var queueArgs = new Pulumi.Aws.Sqs.QueueArgs
        {
            FifoQueue = args?.Fifo ?? false,
            VisibilityTimeoutSeconds = args?.VisibilityTimeoutSeconds ?? 30,
        };

        if (args?.DeadLetterTargetArn != null)
            queueArgs.RedrivePolicy = Output.Format(
                $"{{\"deadLetterTargetArn\":\"{args.DeadLetterTargetArn}\",\"maxReceiveCount\":{args.MaxDeliveryAttempts ?? 5}}}"
            );

        // The provider appends ".fifo" itself when it autonames a FIFO resource, so a suffix
        // in the logical name would end up mid-name and fail AWS name validation.
        var resourceName =
            args?.Fifo == true && name.EndsWith(".fifo") ? name[..^".fifo".Length] : name;
        _queue = new Queue(resourceName, queueArgs, customResourceOptions);

        if (args?.SubscribeTo != null)
        {
            // 1. Link SNS to SQS
            _snsSubscription = new Pulumi.Aws.Sns.TopicSubscription(
                $"{name}-subscription",
                new Pulumi.Aws.Sns.TopicSubscriptionArgs
                {
                    Topic = args.SubscribeTo.SendEndpoint,
                    Protocol = "sqs",
                    Endpoint = _queue.Arn,
                },
                customResourceOptions
            );

            // 2. Add Queue Policy to allow SNS to send messages to this SQS queue
            // This is required for SNS -> SQS cross-service communication
            new Pulumi.Aws.Sqs.QueuePolicy(
                $"{name}-policy",
                new Pulumi.Aws.Sqs.QueuePolicyArgs
                {
                    QueueUrl = _queue.Url,
                    Policy = Output
                        .All(_queue.Arn, args.SubscribeTo.Arn)
                        .Apply(values =>
                        {
                            var queueArn = values[0];
                            var topicArn = values[1];
                            return System.Text.Json.JsonSerializer.Serialize(
                                new
                                {
                                    Version = "2012-10-17",
                                    Statement = new[]
                                    {
                                        new
                                        {
                                            Effect = "Allow",
                                            Principal = new { Service = "sns.amazonaws.com" },
                                            Action = "sqs:SendMessage",
                                            Resource = queueArn,
                                            Condition = new
                                            {
                                                ArnEquals = new { aws_SourceArn = topicArn },
                                            },
                                        },
                                    },
                                }
                            );
                        }),
                },
                customResourceOptions
            );
        }
    }

    public string Name { get; }
    public MessagingKind Kind => MessagingKind.Queue;
    public Output<string> Arn => _queue.Arn;
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> SendEndpoint => _queue.Url;
    public Output<string> ReceiveEndpoint => _queue.Url;

    public Output<int?> MaxDeliveryAttempts => Output.Create((int?)null); // Extract from redrive if needed
    public Output<int?> VisibilityTimeoutSeconds => _queue.VisibilityTimeoutSeconds;
    public Output<string?> DeadLetterTarget => Output.Create((string?)null); // Extract from redrive if needed

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
                            Actions = ["sqs:SendMessage"],
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
                                "sqs:ReceiveMessage",
                                "sqs:DeleteMessage",
                                "sqs:ChangeMessageVisibility",
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
                            Actions = ["sqs:*"],
                            Resources = [Arn],
                        }
                    );
                    break;
            }

        return statements;
    }
}
