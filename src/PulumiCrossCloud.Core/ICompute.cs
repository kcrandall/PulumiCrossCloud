namespace PulumiCrossCloud.Core;

public interface ICompute : ICloudResource, IPolicyProvider<ComputePermissions>
{
    ComputeServiceType ServiceType { get; }
    IEnumerable<ISecret> RequiredSecrets { get; }
    IContainerRegistry ContainerRegistry { get; }
    IDictionary<string, string> EnvironmentVariables { get; }
    
    /// <summary>
    /// Adds a secret that this compute resource requires access to.
    /// Read permissions will be automatically configured when ConfigurePermissions() is called.
    /// </summary>
    void AddRequiredSecret(ISecret secret);
    
    /// <summary>
    /// Adds an environment variable to the compute resource.
    /// </summary>
    void AddEnvironmentVariable(string name, string value);
    
    /// <summary>
    /// Adds multiple environment variables to the compute resource.
    /// </summary>
    void AddEnvironmentVariables(IDictionary<string, string> variables);
    
    /// <summary>
    /// Configures the compute resource with appropriate permissions for its required secrets and container registry.
    /// Each cloud provider handles this differently (AWS roles, Azure managed identities, GCP service accounts).
    /// </summary>
    void ConfigurePermissions();
    
    void Start();
    void Stop();
    void Scale(int replicas);
    void UpdateConfiguration(IDictionary<string, object> config);
}