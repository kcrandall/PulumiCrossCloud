using Amazon.ECR;
using Amazon.ECR.Model;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace PulumiCrossCloud.Integration.Tests.Aws;

[Trait("Category", "Integration")]
[Trait("Cloud", "AWS")]
public sealed class AwsFlociTests(AwsFlociFixture fixture) : IClassFixture<AwsFlociFixture>
{
    [Fact]
    public async Task Redeploying_the_same_program_changes_nothing()
    {
        var preview = await fixture.Stack.PreviewAsync();

        Assert.True(preview.UnexpectedSteps().Count == 0, preview.Diff);
    }

    [Fact]
    public async Task Secret_stores_its_value()
    {
        using var client = new AmazonSecretsManagerClient(
            fixture.Credentials,
            fixture.ClientConfig<AmazonSecretsManagerConfig>()
        );

        var secret = await client.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = fixture.Stack.Output("secretArn") }
        );

        Assert.Equal(AwsFlociFixture.SecretName, secret.Name);
        Assert.Equal(AwsFlociFixture.SecretValue, secret.SecretString);
    }

    [Fact]
    public async Task Bucket_has_versioning_and_cors()
    {
        using var client = S3Client();

        var versioning = await client.GetBucketVersioningAsync(
            new GetBucketVersioningRequest { BucketName = AwsFlociFixture.BucketName }
        );
        var cors = await client.GetCORSConfigurationAsync(
            new GetCORSConfigurationRequest { BucketName = AwsFlociFixture.BucketName }
        );

        Assert.Equal(VersionStatus.Enabled, versioning.VersioningConfig.Status);
        var rule = Assert.Single(cors.Configuration.Rules);
        Assert.Equal(["https://app.floci-it.test"], rule.AllowedOrigins);
        Assert.Equal(["GET", "PUT"], rule.AllowedMethods);
        Assert.Equal(["ETag"], rule.ExposeHeaders);
        Assert.Equal(600, rule.MaxAgeSeconds);
    }

    [Fact]
    public async Task Bucket_round_trips_objects()
    {
        using var client = S3Client();
        // The unversioned bucket, so deleting the object leaves the bucket empty for destroy.
        const string bucket = AwsFlociFixture.PublicBucketName;

        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = "hello.txt",
                ContentBody = "hello from floci",
            }
        );
        try
        {
            using var response = await client.GetObjectAsync(bucket, "hello.txt");
            using var reader = new StreamReader(response.ResponseStream);

            Assert.Equal("hello from floci", await reader.ReadToEndAsync());
        }
        finally
        {
            await client.DeleteObjectAsync(bucket, "hello.txt");
        }
    }

    [Fact]
    public async Task Public_bucket_allows_anonymous_reads()
    {
        using var client = S3Client();

        var accessBlock = await client.GetPublicAccessBlockAsync(
            new GetPublicAccessBlockRequest { BucketName = AwsFlociFixture.PublicBucketName }
        );
        var policy = await client.GetBucketPolicyAsync(
            new GetBucketPolicyRequest { BucketName = AwsFlociFixture.PublicBucketName }
        );

        Assert.False(accessBlock.PublicAccessBlockConfiguration.BlockPublicPolicy);
        Assert.False(accessBlock.PublicAccessBlockConfiguration.RestrictPublicBuckets);
        Assert.Contains("PublicReadGetObject", policy.Policy);
        Assert.Contains($"arn:aws:s3:::{AwsFlociFixture.PublicBucketName}/*", policy.Policy);
    }

    [Fact]
    public async Task Topic_fans_out_to_the_subscribed_queue()
    {
        using var sns = SnsClient();
        using var sqs = SqsClient();
        var message = $"fan-out-{Guid.NewGuid()}";

        await sns.PublishAsync(
            new PublishRequest { TopicArn = fixture.Stack.Output("topicArn"), Message = message }
        );
        var received = await sqs.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = fixture.Stack.Output("queueUrl"),
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 10,
            }
        );

        Assert.Contains(received.Messages ?? [], m => m.Body.Contains(message));
    }

    [Fact]
    public async Task Queue_uses_the_requested_visibility_timeout()
    {
        using var client = SqsClient();

        var attributes = await client.GetQueueAttributesAsync(
            fixture.Stack.Output("queueUrl"),
            ["VisibilityTimeout"]
        );

        Assert.Equal(AwsFlociFixture.QueueVisibilityTimeoutSeconds, attributes.VisibilityTimeout);
    }

    [Fact]
    public async Task Fifo_queue_is_a_valid_fifo_queue()
    {
        using var client = SqsClient();
        var queueUrl = fixture.Stack.Output("fifoQueueUrl");
        var queueName = queueUrl[(queueUrl.LastIndexOf('/') + 1)..];

        var attributes = await client.GetQueueAttributesAsync(queueUrl, ["FifoQueue"]);

        Assert.True(attributes.FifoQueue);
        Assert.EndsWith(".fifo", queueName);
        // Real SQS only accepts alphanumerics, hyphens and underscores before the .fifo suffix.
        Assert.Matches("^[A-Za-z0-9_-]{1,75}\\.fifo$", queueName);
    }

    [Fact]
    public async Task Fifo_topic_is_a_valid_fifo_topic()
    {
        using var client = SnsClient();
        var topicArn = fixture.Stack.Output("fifoTopicArn");
        var topicName = topicArn[(topicArn.LastIndexOf(':') + 1)..];

        var attributes = await client.GetTopicAttributesAsync(topicArn);

        Assert.Equal("true", attributes.Attributes["FifoTopic"]);
        Assert.Equal("true", attributes.Attributes["ContentBasedDeduplication"]);
        // Real SNS only accepts alphanumerics, hyphens and underscores before the .fifo suffix.
        Assert.Matches("^[A-Za-z0-9_-]{1,251}\\.fifo$", topicName);
    }

    [Fact]
    public async Task Fifo_topic_delivers_to_the_subscribed_fifo_queue()
    {
        using var sns = SnsClient();
        using var sqs = SqsClient();
        var message = $"fifo-{Guid.NewGuid()}";

        await sns.PublishAsync(
            new PublishRequest
            {
                TopicArn = fixture.Stack.Output("fifoTopicArn"),
                Message = message,
                MessageGroupId = "integration",
            }
        );
        var received = await sqs.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = fixture.Stack.Output("fifoQueueUrl"),
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 10,
            }
        );

        Assert.Contains(received.Messages ?? [], m => m.Body.Contains(message));
    }

    [Fact]
    public async Task Stream_has_the_requested_shards_and_retention()
    {
        using var client = new AmazonKinesisClient(
            fixture.Credentials,
            fixture.ClientConfig<AmazonKinesisConfig>()
        );

        var summary = await client.DescribeStreamSummaryAsync(
            new DescribeStreamSummaryRequest { StreamName = fixture.Stack.Output("streamName") }
        );

        Assert.Equal(AwsFlociFixture.StreamShards, summary.StreamDescriptionSummary.OpenShardCount);
        Assert.Equal(
            AwsFlociFixture.StreamRetentionHours,
            summary.StreamDescriptionSummary.RetentionPeriodHours
        );
    }

    [Fact]
    public async Task Role_carries_the_attached_inline_policy()
    {
        using var client = new AmazonIdentityManagementServiceClient(
            fixture.Credentials,
            fixture.ClientConfig<AmazonIdentityManagementServiceConfig>()
        );
        var roleArn = fixture.Stack.Output("roleArn");
        var roleName = roleArn[(roleArn.LastIndexOf('/') + 1)..];

        var policies = await client.ListRolePoliciesAsync(
            new ListRolePoliciesRequest { RoleName = roleName }
        );
        var policyName = Assert.Single(policies.PolicyNames);
        var policy = await client.GetRolePolicyAsync(
            new GetRolePolicyRequest { RoleName = roleName, PolicyName = policyName }
        );
        var document = Uri.UnescapeDataString(policy.PolicyDocument);

        Assert.Contains(AwsFlociFixture.RoleInlinePolicyName, policyName);
        Assert.Contains("s3:GetObject", document);
        Assert.Contains("s3:ListBucket", document);
        Assert.Contains($"arn:aws:s3:::{AwsFlociFixture.BucketName}", document);
    }

    [Fact]
    public async Task Registry_repository_exists()
    {
        using var client = new AmazonECRClient(
            fixture.Credentials,
            fixture.ClientConfig<AmazonECRConfig>()
        );

        var repositories = await client.DescribeRepositoriesAsync(
            new DescribeRepositoriesRequest { RepositoryNames = [AwsFlociFixture.RegistryName] }
        );

        var repository = Assert.Single(repositories.Repositories);
        Assert.Equal(fixture.Stack.Output("registryUrl"), repository.RepositoryUri);
    }

    [Fact]
    public async Task Dns_zone_serves_the_registered_record()
    {
        using var client = new AmazonRoute53Client(
            fixture.Credentials,
            fixture.ClientConfig<AmazonRoute53Config>()
        );
        var zoneId = fixture.Stack.Output("zoneId");

        var zone = await client.GetHostedZoneAsync(new GetHostedZoneRequest { Id = zoneId });
        var records = await client.ListResourceRecordSetsAsync(
            new ListResourceRecordSetsRequest { HostedZoneId = zoneId }
        );

        Assert.Equal($"{AwsFlociFixture.ZoneDomain}.", zone.HostedZone.Name);
        var record = Assert.Single(
            records.ResourceRecordSets,
            r => r.Type == RRType.CNAME && r.Name == $"{AwsFlociFixture.ZoneRecordName}."
        );
        Assert.Equal("target.floci-it.test", Assert.Single(record.ResourceRecords).Value);
    }

    private AmazonS3Client S3Client()
    {
        var config = fixture.ClientConfig<AmazonS3Config>();
        config.ForcePathStyle = true;
        return new AmazonS3Client(fixture.Credentials, config);
    }

    private AmazonSimpleNotificationServiceClient SnsClient() =>
        new(fixture.Credentials, fixture.ClientConfig<AmazonSimpleNotificationServiceConfig>());

    private AmazonSQSClient SqsClient() =>
        new(fixture.Credentials, fixture.ClientConfig<AmazonSQSConfig>());
}
