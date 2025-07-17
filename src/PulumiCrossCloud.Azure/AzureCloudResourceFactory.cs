using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureCloudResourceFactory : CloudResourceFactory
{
    public AzureCloudResourceFactory() : base(CloudProvider.Azure)
    {
    }
    
    public override ISecret CreateSecret(string name, string value)
    {
        return new AzureSecret(name, value);
    }
    
    public override IContainerRegistry CreateContainerRegistry(string name)
    {
        return new AzureContainerRegistry(name);
    }
    
    public override ICompute CreateCompute(string name, ComputeServiceType serviceType, IContainerRegistry registry, string imageTag = "latest")
    {
        return serviceType switch
        {
            ComputeServiceType.ContainerApps => new AzureContainerAppsCompute(name, registry, imageTag),
            _ => throw new ArgumentException($"Unsupported Azure service type: {serviceType}")
        };
    }
    
    public override IRole CreateRole(string name, string assumeRolePolicy)
    {
        return new AzureRole(name, assumeRolePolicy);
    }
}