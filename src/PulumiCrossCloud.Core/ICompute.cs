using System;
using Pulumi;

namespace PulumiCrossCloud.Core;

/// <summary>
/// Represents a port forwarding/mapping configuration for a compute resource.
/// </summary>
public struct PortForwardingConfiguration
{
    /// <summary>
    /// The port inside the container to expose.
    /// </summary>
    public int ContainerPort { get; set; }

    /// <summary>
    /// The external port to map to (if supported by the cloud provider). Null means use default mapping.
    /// </summary>
    public int? ExternalPort { get; set; }

    /// <summary>
    /// The protocol for the port (e.g. "TCP", "UDP").
    /// </summary>
    public string? Protocol { get; set; }
}

public interface ICompute : ICloudResource, IPolicyProvider<ComputePermissions>
{
    ComputeServiceType ServiceType { get; }
    IEnumerable<ISecret> RequiredSecrets { get; }
    IContainerRegistry ContainerRegistry { get; }
    IDictionary<string, string> EnvironmentVariables { get; }

    /// <summary>
    /// Public service URL for the running compute (if applicable for the provider).
    /// Implementations that do not expose a URL should return an empty Output<string>.
    /// </summary>
    Output<string> ServiceUrl { get; }

    /// <summary>
    /// Adds a secret that this compute resource requires access to.
    /// Read permissions will be automatically configured when ConfigurePermissions() is called.
    /// Optionally, you can specify an environment variable name to inject the secret value.
    /// </summary>
    void AddRequiredSecret(ISecret secret, string? environmentVariableName = null);

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

    /// <summary>
    /// Returns the DNS records required to add to your domain for this compute resource (e.g. CNAME, TXT for verification).
    /// </summary>
    /// <param name="names">List of domain names (e.g. app.example.com) to map to this compute resource.</param>
    /// <returns>List of DNS record objects (type, name, value).</returns>
    IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names);

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

    /// <summary>
    /// List of port forwarding configurations for the compute resource.
    /// For clouds that only support a single port (e.g. GCP Cloud Run), only the first entry will be used.
    /// </summary>
    IList<PortForwardingConfiguration> PortForwardings { get; set; }

    /// <summary>
    /// Memory limit for the container (e.g. "512Mi", "1Gi", "2Gi"). Provider-specific defaults apply if null.
    /// </summary>
    string? MemoryLimit { get; set; }

    /// <summary>
    /// CPU limit for the container (e.g. "0.5", "1", "2"). Provider-specific defaults apply if null.
    /// </summary>
    string? CpuLimit { get; set; }

    /// <summary>
    /// Registers actions to be executed after the compute resource is healthy.
    /// Each action receives the ICompute instance and is fired after health checks pass.
    /// </summary>
    /// <param name="actions">List of actions to execute post-health-check.</param>
    void OnHealthy(params Func<ICompute, Task>[] actions);

    /// <summary>
    /// Registers actions to be executed after the compute resource is deployed and ready (but before health checks).
    /// Each action receives the ICompute instance and is fired after deployment is successful.
    /// </summary>
    /// <param name="actions">List of actions to execute post-deployment.</param>
    void OnReady(params Func<ICompute, Task>[] actions);

    /// <summary>
    /// Registers custom domains and returns the DNS records needed for domain mapping.
    /// This method allows compute resources to support custom domain mapping with automatic
    /// SSL certificate provisioning where supported by the cloud provider.
    /// </summary>
    /// <param name="customDomains">List of custom domain names (e.g., app.example.com) to map to this compute resource.</param>
    /// <returns>The DNS records that need to be configured in your DNS provider.</returns>
    IEnumerable<DnsRecord> RegisterDnsMappingAndCertificates(IEnumerable<string> customDomains) =>
        throw new NotImplementedException(
            "Domain mapping and certificate registration is not implemented for this compute resource type."
        );
}
