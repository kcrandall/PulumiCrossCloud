using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpCloudRunCompute : ICompute
{
    private readonly Pulumi.Gcp.CloudRun.Service _service;
    private readonly List<ISecret> _requiredSecrets;
    
    public GcpCloudRunCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.CloudRun;
        _requiredSecrets = new List<ISecret>();
        
        // Handle the registry URL as an Output
        var imageUri = containerRegistry is GcpContainerRegistry gcpRegistry 
            ? gcpRegistry.RegistryUrlOutput.Apply(url => $"{url}/{name}:{imageTag}")
            : Output.Create($"PLACEHOLDER_REGISTRY_URL/{name}:{imageTag}");
        
        _service = new Pulumi.Gcp.CloudRun.Service(name, new Pulumi.Gcp.CloudRun.ServiceArgs
        {
            Location = "us-central1",
            Template = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateArgs
            {
                Spec = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecArgs
                {
                    Containers = new[]
                    {
                        new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerArgs
                        {
                            Image = imageUri,
                            Ports = new[]
                            {
                                new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerPortArgs
                                {
                                    ContainerPort = 8080
                                }
                            }
                        }
                    }
                }
            }
        });
    }
    
    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets;
    public Output<string> Arn => _service.Name;
    public CloudProvider Provider => CloudProvider.GCP;
    
    // Add public property to access the service URL
    public Output<string> ServiceUrl => _service.Statuses.Apply(statuses => 
        statuses.FirstOrDefault()?.Url ?? "");
    
    public void AttachRole(IRole role)
    {
        var policies = new List<IPolicyStatement>();
        
        foreach (var secret in RequiredSecrets)
        {
            policies.AddRange(secret.GetPolicy(SecretPermissions.Read));
        }
        
        policies.AddRange(ContainerRegistry.GetPolicy(ContainerRegistryPermissions.Pull));
        
        role.AttachInlinePolicy($"{Name}-policy", policies);
    }
    
    public void AddRequiredSecret(ISecret secret)
    {
        _requiredSecrets.Add(secret);
    }
    
    public void Start() { /* Cloud Run is serverless - auto starts */ }
    public void Stop() { /* Can set traffic to 0 */ }
    public void Scale(int replicas) { /* Update max instances */ }
    public void UpdateConfiguration(IDictionary<string, object> config) { /* Update service spec */ }
    
    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ComputePermissions.GetStatus:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/run.viewer" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.UpdateConfiguration:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/run.developer" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.Scale:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/run.developer" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/logging.viewer" },
                        Resources = new Output<string>[] { Output.Create("*") }
                    });
                    break;
            }
        }
        
        return statements;
    }
}