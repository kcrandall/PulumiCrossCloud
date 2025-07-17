namespace PulumiCrossCloud.Core;

public interface ICompute : ICloudResource, IPolicyProvider<ComputePermissions>
{
    ComputeServiceType ServiceType { get; }
    IEnumerable<ISecret> RequiredSecrets { get; }
    IContainerRegistry ContainerRegistry { get; }
    
    void AttachRole(IRole role);
    void Start();
    void Stop();
    void Scale(int replicas);
    void UpdateConfiguration(IDictionary<string, object> config);
}