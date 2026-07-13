using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.DigitalOcean;

public sealed class DigitalOceanContainerImage : IContainerImage
{
    public DigitalOceanContainerImage(string name, IContainerRegistry registry)
    {
        Name = name;
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));

        if (registry.Provider != CloudProvider.DigitalOcean)
            throw new ArgumentException(
                "Registry must be a DigitalOcean registry.",
                nameof(registry)
            );
    }

    public string Name { get; }
    public Resource Resource { get; set; } = null!;
    public Resource ContainerImageBuildResource { get; set; } = null!;
    public Output<ImageDeploymentResult> DeploymentResult { get; private set; } = null!;
    public Output<string> Arn => DeploymentResult.Apply(result => result.ImageUrl ?? string.Empty);
    public CloudProvider Provider => CloudProvider.DigitalOcean;
    public IContainerRegistry Registry { get; }

    Output<ImageDeploymentResult> IContainerImage.PushImage(
        string dockerfilePath,
        string contextPath,
        ImageArgs? imageArgs
    )
    {
        var buildResource = new ContainerImageBuildResource(
            $"{Registry.Name}-build-{Name}",
            Registry,
            Name,
            dockerfilePath,
            contextPath,
            imageArgs
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

    public InputList<RegistryArgs> GetDockerBuildRegistryConfig() =>
        Registry.GetDockerBuildRegistryConfig();
}
