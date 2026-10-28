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

    [Fact]
    public async Task SourceTagFramesFileNamesAndContents()
    {
        var firstPath = Path.Combine(_contextPath, "a");
        File.WriteAllText(firstPath, "bc");
        var first = await BuildImageAsync();
        File.Delete(firstPath);
        File.WriteAllText(Path.Combine(_contextPath, "ab"), "c");
        var second = await BuildImageAsync();
        Assert.NotEqual(first.Tag, second.Tag);
    }

    [Fact]
    public async Task BucketContentHashFramesFileNamesAndContents()
    {
        string? hash = null;
        async Task<string> BuildBucketAsync()
        {
            await Deployment.TestAsync(
                new ImageBuildMocks(),
                new TestOptions { IsPreview = false },
                () =>
                    new Dictionary<string, object?>
                    {
                        ["hash"] = new TestBucketDeployment(_contextPath).ContentHash.Apply(value =>
                            hash = value
                        ),
                    }
            );
            return Assert.IsType<string>(hash);
        }
        var firstPath = Path.Combine(_contextPath, "a");
        File.WriteAllText(firstPath, "bc");
        var first = await BuildBucketAsync();
        File.Delete(firstPath);
        File.WriteAllText(Path.Combine(_contextPath, "ab"), "c");
        Assert.NotEqual(first, await BuildBucketAsync());
    }

    [Theory]
    [InlineData("*.txt\n!keep.txt\n", "keep.txt", true)]
    [InlineData("*.txt\n!keep.txt\nkeep.txt\n", "keep.txt", false)]
    [InlineData("*.txt\n", "sub/keep.txt", true)]
    [InlineData("**/*.txt\n", "sub/keep.txt", false)]
    [InlineData("**/*.txt\n", "keep.txt", false)]
    [InlineData("/sub/\n!sub/keep.txt\n", "sub/keep.txt", true)]
    public async Task SourceTagFollowsDockerIgnoreRules(
        string patterns,
        string relativePath,
        bool included
    )
    {
        File.WriteAllText(Path.Combine(_contextPath, ".dockerignore"), patterns);
        var file = Path.Combine(_contextPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "before");
        var before = await BuildImageAsync();
        File.WriteAllText(file, "after");
        var after = await BuildImageAsync();
        Assert.Equal(included, before.Tag != after.Tag);
    }

    [Fact]
    public async Task DockerfileSpecificIgnoreOverridesContextIgnore()
    {
        File.WriteAllText(Path.Combine(_contextPath, ".dockerignore"), "*.txt\n");
        File.WriteAllText(Path.Combine(_contextPath, "Dockerfile.dockerignore"), "!keep.txt\n");
        var file = Path.Combine(_contextPath, "keep.txt");
        File.WriteAllText(file, "before");
        var before = await BuildImageAsync();
        File.WriteAllText(file, "after");
        var after = await BuildImageAsync();
        Assert.NotEqual(before.Tag, after.Tag);
    }

    [Fact]
    public async Task ImageUrlTracksExportedDigestEvenWhenSourceTagIsUnchanged()
    {
        var first = await BuildImageAsync("sha256:first");
        var second = await BuildImageAsync("sha256:second");
        Assert.Equal(first.Tag, second.Tag);
        Assert.EndsWith("@sha256:first", first.Url);
        Assert.EndsWith("@sha256:second", second.Url);
        Assert.NotEqual(first.Url, second.Url);
    }

    private async Task<(string Tag, string Url)> BuildImageAsync(string digest = "")
    {
        string? tag = null;
        string? url = null;
        await Deployment.TestAsync(
            new ImageBuildMocks(digest),
            new TestOptions { IsPreview = false },
            () =>
            {
                var image = new ContainerImageBuildResource(
                    "image-build",
                    new TestRegistry(),
                    "app",
                    Path.Combine(_contextPath, "Dockerfile"),
                    _contextPath
                );
                return new Dictionary<string, object?>
                {
                    ["tag"] = image.ImageTag.Apply(value => tag = value),
                    ["url"] = image.ImageUrl.Apply(value => url = value),
                };
            }
        );
        return (Assert.IsType<string>(tag), Assert.IsType<string>(url));
    }

    public void Dispose() => Directory.Delete(_contextPath, recursive: true);

    private sealed class TestBucketDeployment(string path)
        : BucketDeploymentResource("test:index:BucketDeployment", "bucket-deployment", path);

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

    private sealed class ImageBuildMocks(string digest = "") : IMocks
    {
        public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            Resources.Enqueue(args);
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (args.Type == "docker-build:index:Image" && digest.Length > 0)
                state["digest"] = digest;
            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(new Dictionary<string, object>());
    }
}
