using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;

namespace PulumiCrossCloud.Core;

public interface IContainerRegistry : ICloudResource, IPolicyProvider<ContainerRegistryPermissions>
{
    /// <summary>
    /// THe underlying resource representing the container registry.
    /// </summary>
    Resource Resource { get; }

    /// <summary>
    /// Gets the registry URL as an Output for Pulumi deployments.
    /// </summary>
    Output<string> RegistryUrlOutput { get; }

    /// <summary>
    /// Gets registry configuration for DockerBuild with provider-specific authentication
    /// </summary>
    InputList<RegistryArgs> GetDockerBuildRegistryConfig();
}
