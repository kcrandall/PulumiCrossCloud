using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws.Tests;

public class AwsExplicitProviderTests
{
    private const string ProviderName = "explicit";

    [Fact]
    public async Task Factory_registers_every_aws_resource_with_its_explicit_provider()
    {
        var mocks = new ProviderRecordingMocks();

        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                var provider = new Pulumi.Aws.Provider(
                    ProviderName,
                    new Pulumi.Aws.ProviderArgs { Region = "eu-west-1" }
                );
                var factory = new AwsCloudResourceFactory(provider);

                factory.CreateSecret("secret", "value");
                var bucket = factory.CreateBucket(
                    "bucket",
                    isPublic: true,
                    versioningEnabled: true,
                    corsRules: [new CorsRule(["https://app.example.test"], ["GET"], ["*"], [])]
                );
                var topic = factory.CreateTopic("topic");
                factory.CreateQueue("queue", new QueueArgs { SubscribeTo = topic });
                factory.CreateStream("stream");
                var role = factory.CreateRole("role", "{}");
                role.AttachInlinePolicy(
                    "bucket-read",
                    bucket.GetPolicy(BucketPermissions.ReadObject)
                );
                factory.CreateContainerRegistry("registry", createDefaultImage: false);
                var zone = factory.CreateDnsZone("zone", "example.test");
                zone.RegisterRecord(
                    new DnsRecord(
                        DnsRecordType.CNAME,
                        "www.example.test",
                        "target.example.test",
                        "www"
                    )
                );
            }
        );

        var awsResources = mocks
            .Resources.Where(resource => resource.Type?.StartsWith("aws:") == true)
            .ToList();
        var onOtherProviders = awsResources
            .Where(resource => !UsesProvider(resource, "aws", ProviderName))
            .Select(resource => $"{resource.Type}::{resource.Name} -> {resource.Provider}")
            .ToList();

        Assert.True(
            onOtherProviders.Count == 0,
            string.Join(Environment.NewLine, onOtherProviders)
        );
        Assert.Contains(awsResources, resource => resource.Type == "aws:iam/rolePolicy:RolePolicy");
        Assert.Contains(awsResources, resource => resource.Type == "aws:route53/record:Record");
    }

    private static bool UsesProvider(MockResourceArgs resource, string package, string name) =>
        resource.Provider?.Contains($"pulumi:providers:{package}::{name}::") == true;

    private sealed class ProviderRecordingMocks : IMocks
    {
        private readonly ConcurrentQueue<MockResourceArgs> _resources = new();

        public IEnumerable<MockResourceArgs> Resources => _resources;

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            _resources.Enqueue(args);
            var name = args.Name ?? "resource";
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            state.TryAdd("name", name);
            state.TryAdd("arn", $"arn:aws:mock:eu-west-1:123456789012:{name}");
            return Task.FromResult<(string? id, object state)>(($"{name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
    }
}
