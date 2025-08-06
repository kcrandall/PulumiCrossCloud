using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpContainerImage : IContainerImage
{
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
        {
            throw new ArgumentException("Registry must be a GCP registry", nameof(registry));
        }
    }

    Output<ImageDeploymentResult> PushImage(string dockerfilePath, string contextPath,
        IEnumerable<string> additionalFilesToHash, ImageArgs? imageArgs = null)
    {
        var result= ((IContainerImage)this).PushImage(dockerfilePath, contextPath, additionalFilesToHash, imageArgs);
        DeploymentResult = result;
        return result;
    }


    /// <summary>
    /// GCP-specific registry configuration - uses default gcloud authentication
    /// </summary>
    public InputList<RegistryArgs> GetDockerBuildRegistryConfig()
    {
        // GCP uses default gcloud authentication, return empty list
        return new InputList<RegistryArgs>();
    }
}