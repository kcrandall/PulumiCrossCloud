using System.Collections.Concurrent;
using Pulumi;
using Pulumi.DockerBuild.Inputs;
using Pulumi.Testing;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Core.Tests;

[Collection("Pulumi deployments")]
public sealed class ContainerImageBuildResourceTests : IDisposable
{
    private readonly string _contextPath = Path.Combine(
        Path.GetTempPath(),
        $"crosscloud-image-tests-{Guid.NewGuid():N}"
    );

    public ContainerImageBuildResourceTests()
    {
        Directory.CreateDirectory(_contextPath);
        File.WriteAllText(Path.Combine(_contextPath, "Dockerfile"), "FROM scratch\n");
    }

    [Fact]
    public async Task MultipleImagesInOneRegistry_HaveDistinctNamesAndProvidersInEachDeployment()
    {
        for (var deployment = 0; deployment < 2; deployment++)
        {
            var mocks = new ImageBuildMocks();
            await Deployment.TestAsync(
                mocks,
                new TestOptions { IsPreview = false },
                () =>
                {
                    var registry = new TestRegistry();
                    _ = new ContainerImageBuildResource(
                        "first-build",
                        registry,
                        "first",
                        Path.Combine(_contextPath, "Dockerfile"),
                        _contextPath
                    );
                    _ = new ContainerImageBuildResource(
                        "second-build",
                        registry,
                        "second",
                        Path.Combine(_contextPath, "Dockerfile"),
                        _contextPath
                    );
                }
            );

            var imageNames = mocks
                .Resources.Where(resource => resource.Type == "docker-build:index:Image")
                .Select(resource => resource.Name)
                .OrderBy(name => name)
                .ToArray();
            Assert.Collection(
                imageNames,
                name => Assert.Equal("first-build-image", name),
                name => Assert.Equal("second-build-image", name)
            );
            Assert.Equal(
                2,
                mocks.Resources.Count(resource => resource.Type == "pulumi:providers:docker-build")
            );
        }
    }

    public void Dispose() => Directory.Delete(_contextPath, recursive: true);

    private sealed class TestRegistry : IContainerRegistry
    {
        public string Name => "shared-registry";
        public CloudProvider Provider => CloudProvider.GCP;
        public Resource Resource { get; } =
            new ComponentResource("test:index:Registry", "registry");
        public Output<string> Arn => Resource.Urn;
        public Output<string> RegistryUrlOutput => Output.Create("registry.example.com/project");

        public InputList<RegistryArgs> GetDockerBuildRegistryConfig() => [];

        public IEnumerable<IPolicyStatement> GetPolicy(
            params ContainerRegistryPermissions[] permissions
        ) => [];
    }

    private sealed class ImageBuildMocks : IMocks
    {
        public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            Resources.Enqueue(args);
            return Task.FromResult<(string? id, object state)>(
                ($"{args.Name}-id", args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value))
            );
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(new Dictionary<string, object>());
    }
}
