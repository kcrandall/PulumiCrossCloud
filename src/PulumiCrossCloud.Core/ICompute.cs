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
    /// Adds a custom policy statement to the compute resource.
    /// </summary>
    void AddPolicyStatement(IPolicyStatement statement);
    
    /// <summary>
    /// Finalizes and builds the compute resource, applying all configuration and dependencies.
    /// </summary>
    void Build();
    
    void Start();
    void Stop();
    void Scale(int replicas);

    /// <summary>
    /// The minimum number of compute instances to run.
    /// </summary>
    int MinCount { get; set; }
    /// <summary>
    /// The maximum number of compute instances to run.
    /// </summary>
    int MaxCount { get; set; }

    /// <summary>
    /// Optional health check configuration for the compute resource.
    /// </summary>
    IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
}