using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp.Tests;

public class GcpExplicitProviderTests
{
    private const string ProjectServiceType = "gcp:projects/service:Service";

    public GcpExplicitProviderTests()
    {
        GcpProjectConfig.ProjectId = CloudRunDeployment.ProjectId;
        GcpProjectConfig.Region = CloudRunDeployment.Region;
    }

    [Fact]
    public async Task Factory_registers_every_gcp_resource_with_its_explicit_provider()
    {
        var mocks = new ProviderRecordingMocks();

        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                var factory = new GcpCloudResourceFactory(Provider("explicit"));

                factory.CreateSecret("secret", "value");
                factory.CreateBucket("bucket", isPublic: true, versioningEnabled: true);
                var topic = factory.CreateTopic("topic");
                factory.CreateQueue("queue", new QueueArgs { SubscribeTo = topic });
                factory.CreateQueue("standalone-queue");
                factory.CreateStream("stream");
                factory.CreateRole("service-account", assumeRolePolicy: "");
                var registry = factory.CreateContainerRegistry("registry");
                factory.CreateCompute("api", registry, "api", "v1").Build();
                var zone = factory.CreateDnsZone("zone", "example.test");
                zone.RegisterRecord(
                    new DnsRecord(
                        DnsRecordType.CNAME,
                        "www.example.test.",
                        "target.example.test.",
                        "www"
                    )
                );
            }
        );

        var gcpResources = mocks
            .Resources.Where(resource => resource.Type?.StartsWith("gcp:") == true)
            .ToList();
        var onOtherProviders = gcpResources
            .Where(resource => !UsesProvider(resource, "explicit"))
            .Select(resource => $"{resource.Type}::{resource.Name} -> {resource.Provider}")
            .ToList();

        Assert.True(
            onOtherProviders.Count == 0,
            string.Join(Environment.NewLine, onOtherProviders)
        );
        Assert.Contains(gcpResources, resource => resource.Type == ProjectServiceType);
        Assert.Contains(gcpResources, resource => resource.Type == "gcp:pubsub/topic:Topic");
        Assert.Contains(gcpResources, resource => resource.Type == "gcp:dns/recordSet:RecordSet");
    }

    [Fact]
    public async Task Each_provider_enables_project_apis_through_itself()
    {
        var mocks = new ProviderRecordingMocks();

        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                new GcpCloudResourceFactory(Provider("first")).CreateSecret(
                    "first-secret",
                    "value"
                );
                new GcpCloudResourceFactory(Provider("second")).CreateSecret(
                    "second-secret",
                    "value"
                );
            }
        );

        var services = mocks
            .Resources.Where(resource => resource.Type == ProjectServiceType)
            .ToList();

        foreach (var provider in new[] { "first", "second" })
        {
            var enabledApis = services
                .Where(resource => UsesProvider(resource, provider))
                .Select(resource => (string)resource.Inputs["service"])
                .Order()
                .ToList();

            Assert.Equal(
                ["cloudresourcemanager.googleapis.com", "secretmanager.googleapis.com"],
                enabledApis
            );
        }
    }

    [Fact]
    public async Task Default_provider_keeps_the_existing_api_resource_names()
    {
        var mocks = new ProviderRecordingMocks();

        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () => new GcpCloudResourceFactory().CreateSecret("secret", "value")
        );

        var names = mocks
            .Resources.Where(resource => resource.Type == ProjectServiceType)
            .Select(resource => resource.Name)
            .Order()
            .ToList();

        Assert.Equal(
            [
                $"cloudresourcemanager-googleapis-com-api-{CloudRunDeployment.ProjectId}",
                $"secretmanager-googleapis-com-api-{CloudRunDeployment.ProjectId}",
            ],
            names
        );
    }

    private static Pulumi.Gcp.Provider Provider(string name) =>
        new(name, new Pulumi.Gcp.ProviderArgs { Project = CloudRunDeployment.ProjectId });

    private static bool UsesProvider(MockResourceArgs resource, string name) =>
        resource.Provider?.Contains($"pulumi:providers:gcp::{name}::") == true;

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
            state.TryAdd("email", $"{name}@{CloudRunDeployment.ProjectId}.iam.gserviceaccount.com");
            state.TryAdd("uri", $"https://{name}.a.run.app");
            return Task.FromResult<(string? id, object state)>(($"{name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
    }
}
