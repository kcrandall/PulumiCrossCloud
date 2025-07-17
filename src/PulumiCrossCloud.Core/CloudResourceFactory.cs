namespace PulumiCrossCloud.Core;

public abstract class CloudResourceFactory
{
    protected readonly CloudProvider _provider;
    
    protected CloudResourceFactory(CloudProvider provider)
    {
        _provider = provider;
    }
    
    public abstract ISecret CreateSecret(string name, string value);
    public abstract IContainerRegistry CreateContainerRegistry(string name);
    public abstract ICompute CreateCompute(string name, ComputeServiceType serviceType, IContainerRegistry registry, string imageTag = "latest");
    public abstract IRole CreateRole(string name, string assumeRolePolicy);
}