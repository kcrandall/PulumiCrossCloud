using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureContainerAppsCompute : ICompute
{
    private readonly List<ISecret> _requiredSecrets;
    
    public AzureContainerAppsCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.ContainerApps;
        _requiredSecrets = new List<ISecret>();
        
        // Azure Container Apps implementation would go here
        Arn = Output.Create($"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/containerApps/{name}");
        
        // Handle the registry URL as an Output
        var imageUri = containerRegistry is AzureContainerRegistry azureRegistry 
            ? azureRegistry.RegistryUrlOutput.Apply(url => $"{url}/{name}:{imageTag}")
            : Output.Create($"PLACEHOLDER_REGISTRY_URL/{name}:{imageTag}");
    }
    
    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets;
    public Output<string> Arn { get; private set; }  // Changed to Output<string>
    public CloudProvider Provider => CloudProvider.Azure;
    
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
    
    public void Start() => throw new NotImplementedException("Azure Container Apps implementation needed");
    public void Stop() => throw new NotImplementedException("Azure Container Apps implementation needed");
    public void Scale(int replicas) => throw new NotImplementedException("Azure Container Apps implementation needed");
    public void UpdateConfiguration(IDictionary<string, object> config) => throw new NotImplementedException("Azure Container Apps implementation needed");
    
    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ComputePermissions.GetStatus:
                    statements.Add(new AzurePolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "Microsoft.App/containerApps/read" },
                        Resources = new Output<string>[] { Arn }  // Fixed to use Output<string>[]
                    });
                    break;
                case ComputePermissions.Scale:
                    statements.Add(new AzurePolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "Microsoft.App/containerApps/write" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.UpdateConfiguration:
                    statements.Add(new AzurePolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "Microsoft.App/containerApps/write" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(new AzurePolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "Microsoft.Insights/logs/read" },
                        Resources = new Output<string>[] { Output.Create("*") }  // Convert string to Output<string>
                    });
                    break;
            }
        }
        
        return statements;
    }
}