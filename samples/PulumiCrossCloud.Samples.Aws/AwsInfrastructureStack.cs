using Pulumi;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Aws;

namespace PulumiCrossCloud.Samples.Aws;

public class AwsInfrastructureStack : Stack
{
    public AwsInfrastructureStack()
    {
        var factory = new AwsCloudResourceFactory();
        
        // Create resources - these will only be created once
        var secret = factory.CreateSecret("app-secret", "my-secret-value");
        var registry = factory.CreateContainerRegistry("app-registry");
        
        // Create different compute types based on needs
        var appRunnerService = factory.CreateCompute("web-app",  registry, "web-app-image", "latest", serviceType: ComputeServiceType.AppRunner);
        
        var role = factory.CreateRole("app-role", @"{
            ""Version"": ""2012-10-17"",
            ""Statement"": [{
                ""Effect"": ""Allow"",
                ""Principal"": {
                    ""Service"": [""apprunner.amazonaws.com"", ""ecs-tasks.amazonaws.com""]
                },
                ""Action"": ""sts:AssumeRole""
            }]
        }");
        
        // Add secret dependencies
        ((AwsAppRunnerCompute)appRunnerService).AddRequiredSecret(secret);
        
        
        // Export some values - using public properties and Output types
        this.AppRunnerUrl = ((AwsAppRunnerCompute)appRunnerService).ServiceUrl;
        this.RegistryUrl = ((AwsContainerRegistry)registry).RegistryUrlOutput;
    }
    
    [Output] public Output<string> AppRunnerUrl { get; set; }
    [Output] public Output<string> RegistryUrl { get; set; }
}