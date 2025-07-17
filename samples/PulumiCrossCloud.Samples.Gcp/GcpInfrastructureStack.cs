using Pulumi;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Gcp;

namespace PulumiCrossCloud.Samples.Gcp;

public class GcpInfrastructureStack : Stack
{
    public GcpInfrastructureStack()
    {
        var factory = new GcpCloudResourceFactory();
        
        // Create resources
        var secret = factory.CreateSecret("app-secret", "my-secret-value");
        var registry = factory.CreateContainerRegistry("app-registry");
        var cloudRunService = factory.CreateCompute("web-app", ComputeServiceType.CloudRun, registry);
        
        // Add secret dependency
        ((GcpCloudRunCompute)cloudRunService).AddRequiredSecret(secret);
        
        // Export values - using public properties and Output types
        this.CloudRunUrl = ((GcpCloudRunCompute)cloudRunService).ServiceUrl;
        this.RegistryUrl = ((GcpContainerRegistry)registry).RegistryUrlOutput;
    }
    
    [Output] public Output<string> CloudRunUrl { get; set; }
    [Output] public Output<string> RegistryUrl { get; set; }
}