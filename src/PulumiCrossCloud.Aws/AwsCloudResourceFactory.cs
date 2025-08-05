using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsCloudResourceFactory : CloudResourceFactory
{
    public AwsCloudResourceFactory() : base(CloudProvider.AWS)
    {
    }
    
    public override ISecret CreateSecret(string name, string value, bool ignoreChangesAfterCreate = false)
    {
        return new AwsSecret(name, value);
    }
    
    public override IContainerRegistry CreateContainerRegistry(string name, bool createDefaultImage = true)
    {
        return new AwsContainerRegistry(name);
    }
    
    public override ICompute CreateCompute(string name, ComputeServiceType serviceType, IContainerRegistry registry, string imageTag = "latest")
    {
        return serviceType switch
        {
            ComputeServiceType.AppRunner => new AwsAppRunnerCompute(name, registry, imageTag),
            ComputeServiceType.Fargate => new AwsAppRunnerCompute(name, registry, imageTag),
            _ => throw new ArgumentException($"Unsupported AWS service type: {serviceType}")
        };
    }
    
    public override IRole CreateRole(string name, string assumeRolePolicy)
    {
        return new AwsRole(name, assumeRolePolicy);
    }
}