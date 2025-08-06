using Pulumi;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Gcp;

namespace PulumiCrossCloud.Samples.Gcp;

public class GcpInfrastructureStack : Stack
{
    public GcpInfrastructureStack()
    {
        // Set GCP config programmatically for the sample
        // GcpProjectConfig.ProjectId = "your-project-id";
        // GcpProjectConfig.Region = "us-central1";
        // GcpProjectConfig.Zone = "us-central1-a";
        
        // Initialize the GCP cloud resource factory
        var factory = new GcpCloudResourceFactory();
        
        // Create resources
        var secret = factory.CreateSecret("app-secret", "my-secret-value", ignoreChangesAfterCreate: true);
        var registry = factory.CreateContainerRegistry("app-registry",  "app-registry", createDefaultImage: true);
        var cloudRunService = factory.CreateCompute("web-app", registry, "web-app-image", "latest", serviceType: ComputeServiceType.CloudRun);
        
        // Add secret dependency
        ((GcpCloudRunCompute)cloudRunService).AddRequiredSecret(secret);
        
        // Export values - using public properties and Output types
        this.CloudRunUrl = ((GcpCloudRunCompute)cloudRunService).ServiceUrl;
        this.RegistryUrl = ((GcpContainerRegistry)registry).RegistryUrlOutput;
    }
    
    [Output] public Output<string> CloudRunUrl { get; set; }
    [Output] public Output<string> RegistryUrl { get; set; }
}