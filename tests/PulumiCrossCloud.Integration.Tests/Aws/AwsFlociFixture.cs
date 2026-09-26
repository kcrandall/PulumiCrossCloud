using Amazon.Runtime;
using Pulumi.Automation;
using PulumiCrossCloud.Aws;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Integration.Tests.Infrastructure;

namespace PulumiCrossCloud.Integration.Tests.Aws;

/// <summary>
/// Deploys every AWS resource Floci can host through <see cref="AwsCloudResourceFactory"/> once,
/// and destroys it after the last test in <see cref="AwsFlociTests"/>.
/// </summary>
public sealed class AwsFlociFixture : IAsyncLifetime
{
    public const string Region = "us-east-1";
    public const string SecretName = "it-secret";
    public const string SecretValue = "floci-aws-secret-value";
    public const string BucketName = "it-bucket";
    public const string PublicBucketName = "it-public-bucket";
    public const string RoleInlinePolicyName = "bucket-read";
    public const string RegistryName = "it-registry";
    public const string ZoneDomain = "floci-it.test";
    public const string ZoneRecordName = "www.floci-it.test";
    public const int QueueVisibilityTimeoutSeconds = 45;
    public const int StreamShards = 2;
    public const int StreamRetentionHours = 48;

    // Every service endpoint the deployed resources call; anything missing would reach real AWS.
    private static readonly string[] EndpointServices =
    [
        "ecr",
        "iam",
        "kinesis",
        "route53",
        "s3",
        "secretsmanager",
        "sns",
        "sqs",
        "sts",
    ];

    private FlociEmulator? _emulator;

    public PulumiStack Stack { get; private set; } = null!;
    public string Endpoint => _emulator!.Endpoint;

    public async Task InitializeAsync()
    {
        _emulator = await FlociEmulator.StartAwsAsync();

        Dictionary<string, string> config = new()
        {
            ["aws:region"] = Region,
            ["aws:accessKey"] = "test",
            ["aws:secretKey"] = "test",
            ["aws:skipCredentialsValidation"] = "true",
            ["aws:skipMetadataApiCheck"] = "true",
            ["aws:skipRequestingAccountId"] = "true",
            ["aws:s3UsePathStyle"] = "true",
        };
        foreach (var service in EndpointServices)
            config[$"aws:endpoints[0].{service}"] = _emulator.Endpoint;

        Stack = await PulumiStack.CreateAsync(
            "pulumicrosscloud-aws-floci",
            PulumiFn.Create(Program),
            config
        );
        await Stack.UpAsync();
    }

    private static IDictionary<string, object?> Program()
    {
        var factory = new AwsCloudResourceFactory();

        var secret = factory.CreateSecret(SecretName, SecretValue);

        var bucket = factory.CreateBucket(
            BucketName,
            versioningEnabled: true,
            corsRules:
            [
                new CorsRule(["https://app.floci-it.test"], ["GET", "PUT"], ["*"], ["ETag"], 600),
            ]
        );
        factory.CreateBucket(PublicBucketName, isPublic: true);

        var topic = factory.CreateTopic("it-topic");
        var queue = factory.CreateQueue(
            "it-queue",
            new QueueArgs
            {
                SubscribeTo = topic,
                VisibilityTimeoutSeconds = QueueVisibilityTimeoutSeconds,
            }
        );
        var fifoTopic = factory.CreateTopic(
            "it-fifo-topic",
            new TopicArgs { Fifo = true, ContentBasedDeduplication = true }
        );
        var fifoQueue = factory.CreateQueue(
            "it-fifo",
            new QueueArgs { Fifo = true, SubscribeTo = fifoTopic }
        );

        var stream = factory.CreateStream(
            "it-stream",
            new StreamArgs { ThroughputUnits = StreamShards, RetentionHours = StreamRetentionHours }
        );

        var role = factory.CreateRole(
            "it-role",
            """
            {
              "Version": "2012-10-17",
              "Statement": [
                {
                  "Effect": "Allow",
                  "Principal": { "Service": "ecs-tasks.amazonaws.com" },
                  "Action": "sts:AssumeRole"
                }
              ]
            }
            """
        );
        role.AttachInlinePolicy(
            RoleInlinePolicyName,
            bucket.GetPolicy(BucketPermissions.ReadObject, BucketPermissions.ListBucket)
        );

        var registry = factory.CreateContainerRegistry(RegistryName, createDefaultImage: false);

        var zone = factory.CreateDnsZone("it-zone", ZoneDomain);
        zone.RegisterRecord(
            new DnsRecord(DnsRecordType.CNAME, ZoneRecordName, "target.floci-it.test", "www")
        );

        return new Dictionary<string, object?>
        {
            ["secretArn"] = secret.Arn,
            ["topicArn"] = topic.Arn,
            ["queueUrl"] = queue.ReceiveEndpoint,
            ["fifoTopicArn"] = fifoTopic.Arn,
            ["fifoQueueUrl"] = fifoQueue.ReceiveEndpoint,
            ["streamName"] = stream.ReceiveEndpoint,
            ["roleArn"] = role.Arn,
            ["registryUrl"] = registry.RegistryUrlOutput,
            ["zoneId"] = zone.ZoneId,
        };
    }

    public AWSCredentials Credentials { get; } = new BasicAWSCredentials("test", "test");

    public TConfig ClientConfig<TConfig>()
        where TConfig : ClientConfig, new() =>
        new() { ServiceURL = Endpoint, AuthenticationRegion = Region };

    public async Task DisposeAsync()
    {
        try
        {
            if (Stack is not null)
                await Stack.DisposeAsync();
        }
        finally
        {
            if (_emulator is not null)
                await _emulator.DisposeAsync();
        }
    }
}
