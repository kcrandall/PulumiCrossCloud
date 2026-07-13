using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;

namespace PulumiCrossCloud.Core;

public class ImageDeploymentResult
{
    public string? ImageUrl { get; set; }
    public string? ImageTag { get; set; }

    public Image? Image { get; set; }
}

public interface IContainerImage : ICloudResource
{
    /// <summary>
    /// THe underlying resource representing the container image (docker image).
    /// </summary>
    Resource Resource { get; protected set; }
    Resource ContainerImageBuildResource { get; protected set; }

    /// <summary>
    /// Gets the image tag based on content hash
    /// </summary>
    Output<ImageDeploymentResult> DeploymentResult { get; }

    /// <summary>
    /// Gets the registry this image belongs to
    /// </summary>
    IContainerRegistry Registry { get; }

    /// <summary>
    /// Deploys a Docker image to the registry from a Dockerfile and context, tracking file hashes for redeployment.
    /// </summary>
    /// <param name="dockerfilePath">Path to the Dockerfile.</param>
    /// <param name="contextPath">Path to the build context.</param>
    /// <param name="additionalFilesToHash">Additional files to include in the hash.</param>
    /// <param name="imageArgs">Build args for different cloud's auth etc</param>
    /// <returns>Output containing the deployed image URL.</returns>
    Output<ImageDeploymentResult> PushImage(
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
            imageArgs
        );
        Resource = buildResource.Resource;
        ContainerImageBuildResource = buildResource;

        var deploymentResult = Output
            .Tuple(buildResource.ImageUrl, buildResource.ImageTag, buildResource.ResourceOutput)
            .Apply(tuple => new ImageDeploymentResult
            {
                ImageUrl = tuple.Item1,
                ImageTag = tuple.Item2,
                Image = tuple.Item3,
            });
        return deploymentResult;
    }

    /// <summary>
    /// Gets registry configuration for DockerBuild with provider-specific authentication
    /// </summary>
    InputList<RegistryArgs> GetDockerBuildRegistryConfig()
    {
        return new InputList<RegistryArgs>();
    }
}
