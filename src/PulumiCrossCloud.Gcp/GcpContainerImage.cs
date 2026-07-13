using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpContainerImage : IContainerImage
{
    public Resource Resource { get; set; }
    public Resource ContainerImageBuildResource { get; set; }

    public string Name { get; }
    public Output<ImageDeploymentResult> DeploymentResult { get; protected set; }
    public Output<string> Arn => DeploymentResult.Apply(result => result.ImageUrl ?? string.Empty);
    public CloudProvider Provider => CloudProvider.GCP;
    public IContainerRegistry Registry { get; }

    public GcpContainerImage(string name, IContainerRegistry registry)
    {
        Name = name;
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));

        if (registry.Provider != CloudProvider.GCP)
            throw new ArgumentException("Registry must be a GCP registry", nameof(registry));

        // Initialize properties to satisfy compiler warnings
        Resource = null!; // Will be set by PushImage method
        ContainerImageBuildResource = null!; // Will be set by PushImage method
        DeploymentResult = null!; // Will be set by PushImage method
    }

    Output<ImageDeploymentResult> PushImage(
        string dockerfilePath,
        string contextPath,
        ImageArgs? imageArgs = null
    )
    {
        var result = ((IContainerImage)this).PushImage(dockerfilePath, contextPath, imageArgs);
        DeploymentResult = result;
        return result;
    }

    /// <summary>
    /// GCP-specific registry configuration - delegates to the registry for authentication
    /// </summary>
    public InputList<RegistryArgs> GetDockerBuildRegistryConfig()
    {
        // Delegate to the registry for proper authentication
        if (Registry is GcpContainerRegistry gcpRegistry)
            return gcpRegistry.GetDockerBuildRegistryConfig();

        // Fallback to empty list if not a GCP registry
        return new InputList<RegistryArgs>();
    }
}
