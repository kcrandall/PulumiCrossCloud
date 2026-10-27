using Pulumi;
using Pulumi.DockerBuild.Inputs;
using Pulumi.Testing;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp.Tests;

public sealed class GcpContainerImageTests
{
    [Fact]
    public async Task PushImage_ThroughTheInterface_PersistsTheResultConsumedByCloudRun()
    {
        var contextPath = Path.Combine(Path.GetTempPath(), $"gcp-image-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contextPath);
        var dockerfilePath = Path.Combine(contextPath, "Dockerfile");
        await File.WriteAllTextAsync(dockerfilePath, "FROM scratch\n");

        try
        {
            var mocks = new RecordingMocks();
            await Deployment.TestAsync(
                mocks,
                new TestOptions { IsPreview = false },
                () =>
                {
                    GcpProjectConfig.ProjectId = CloudRunDeployment.ProjectId;
                    GcpProjectConfig.Region = CloudRunDeployment.Region;
                    var registry = new TestRegistry();
                    IContainerImage image = new GcpContainerImage("api", registry);
                    var result = image.PushImage(dockerfilePath, contextPath);
                    Assert.Same(result, image.DeploymentResult);
                    new GcpCloudRunCompute("pushed-api", registry, containerImage: image).Build();
                }
            );

            var service = Assert.Single(mocks.Created(CloudRunDeployment.CloudRunServiceType));
            Assert.True(
                PulumiPropertyPath.TryResolve(
                    service.Inputs,
                    GcpCloudRunCompute.ContainerImagePropertyPath,
                    out var imageUri
                )
            );
            Assert.StartsWith("example.test/images/api:sha-", Assert.IsType<string>(imageUri));
        }
        finally
        {
            Directory.Delete(contextPath, recursive: true);
        }
    }

    private sealed class TestRegistry : IContainerRegistry
    {
        public string Name => "test-registry";
        public Output<string> Arn => Output.Create("registry-id");
        public CloudProvider Provider => CloudProvider.GCP;
        public Resource Resource { get; } =
            new ComponentResource("tests:index:Registry", "registry");
        public Output<string> RegistryUrlOutput => Output.Create("example.test/images");

        public InputList<RegistryArgs> GetDockerBuildRegistryConfig() => [];

        public IEnumerable<IPolicyStatement> GetPolicy(
            params ContainerRegistryPermissions[] permissions
        ) => [];
    }
}
