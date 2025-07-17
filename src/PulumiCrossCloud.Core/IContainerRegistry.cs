using Pulumi;

namespace PulumiCrossCloud.Core;

public interface IContainerRegistry : ICloudResource, IPolicyProvider<ContainerRegistryPermissions>
{
    /// <summary>
    /// Gets the registry URL. May throw NotImplementedException in Pulumi contexts.
    /// Use RegistryUrlOutput for deployment scenarios.
    /// </summary>
    string RegistryUrl { get; }
    
    /// <summary>
    /// Gets the registry URL as an Output for Pulumi deployments.
    /// </summary>
    Output<string> RegistryUrlOutput { get; }
    
    void PushImage(string imageName, string tag);
    void PullImage(string imageName, string tag);
    IEnumerable<string> ListRepositories();
    void DeleteRepository(string repositoryName);
}