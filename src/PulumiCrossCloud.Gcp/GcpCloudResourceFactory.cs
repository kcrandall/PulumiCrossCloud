using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpCloudResourceFactory : CloudResourceFactory
{
    public GcpCloudResourceFactory() : base(CloudProvider.GCP)
    {
    }
    
    public override ISecret CreateSecret(string name, string value, bool ignoreChangesAfterCreate = false)
    {
        return new GcpSecret(name, value, ignoreChangesAfterCreate);
    }
    
    public override IContainerRegistry CreateContainerRegistry(string name, bool createDefaultImage = true)
    {
        return new GcpContainerRegistry(name, createDefaultImage);
    }
    
    public override ICompute CreateCompute(string name, IContainerRegistry registry, string imageTag = "latest", ComputeServiceType? serviceType = null)
    {
        if (serviceType is null)
        {
            serviceType = ComputeServiceType.CloudRun;
        }
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