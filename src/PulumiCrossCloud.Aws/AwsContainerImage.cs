using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsContainerImage : IContainerImage
{
    public Resource Resource { get; set; } = null!;
    public Resource ContainerImageBuildResource { get; set; } = null!;
    public string Name { get; }
    public Output<string> Arn => DeploymentResult.Apply(result => result.ImageUrl ?? string.Empty);
    public CloudProvider Provider => CloudProvider.AWS;
    public IContainerRegistry Registry { get; }
    public Output<ImageDeploymentResult> DeploymentResult { get; private set; } = null!;

    public AwsContainerImage(string name, IContainerRegistry registry)
    {
        Name = name;
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));

        if (registry.Provider != CloudProvider.AWS)
            throw new ArgumentException("Registry must be an AWS registry", nameof(registry));
    }

    public Output<ImageDeploymentResult> PushImage(
        string dockerfilePath,
        string contextPath,
        ImageArgs? imageArgs = null
    )
    {
        var buildResource = new ContainerImageBuildResource(
            $"{Registry.Name}-build-{Name}",
            Registry,
            Name,
            dockerfilePath,
            contextPath,
            imageArgs,
            appendImageNameToRegistryUrl: false
        );
        Resource = buildResource.Resource;
        ContainerImageBuildResource = buildResource;
        DeploymentResult = Output
            .Tuple(buildResource.ImageUrl, buildResource.ImageTag, buildResource.ResourceOutput)
            .Apply(tuple => new ImageDeploymentResult
            {
                ImageUrl = tuple.Item1,
                ImageTag = tuple.Item2,
                Image = tuple.Item3,
            });
        return DeploymentResult;
    }

    /// <summary>
    /// AWS ECR-specific registry configuration - requires authentication credentials
    /// </summary>
    public InputList<RegistryArgs> GetDockerBuildRegistryConfig() =>
        Registry.GetDockerBuildRegistryConfig();
}
