using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpCloudResourceFactory : CloudResourceFactory
{
    public GcpCloudResourceFactory() : base(CloudProvider.GCP)
    {
    }
    
    public override ISecret CreateSecret(string name, string value)
    {
        return new GcpSecret(name, value);
    }
    
    public override IContainerRegistry CreateContainerRegistry(string name)
    {
        return new GcpContainerRegistry(name);
    }
    
    public override ICompute CreateCompute(string name, ComputeServiceType serviceType, IContainerRegistry registry, string imageTag = "latest")
    {
        return serviceType switch
        {
            ComputeServiceType.CloudRun => new GcpCloudRunCompute(name, registry, imageTag),
            _ => throw new ArgumentException($"Unsupported GCP service type: {serviceType}")
        };
    }
    
    public override IRole CreateRole(string name, string assumeRolePolicy)
    {
        // GCP doesn't have assume role policies like AWS
        // Create a service account instead, which is the GCP equivalent
        return new GcpServiceAccount(name);
    }
}