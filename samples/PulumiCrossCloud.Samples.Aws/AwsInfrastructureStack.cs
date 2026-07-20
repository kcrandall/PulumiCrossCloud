using Pulumi;
using PulumiCrossCloud.Aws;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Samples.Aws;

public class AwsInfrastructureStack : Stack
{
    public AwsInfrastructureStack()
    {
        var factory = new AwsCloudResourceFactory();

        // Create resources - these will only be created once
        var secret = factory.CreateSecret("app-secret", "my-secret-value");
        var registry = factory.CreateContainerRegistry("app-registry");
        var image = factory.CreateContainerImage("web-app-image", registry);

        // Build a runnable image into ECR before the Fargate service is created.
        var containerContext = Path.Combine(AppContext.BaseDirectory, "container");
        image.PushImage(Path.Combine(containerContext, "Dockerfile"), containerContext);

        // With no cluster name, Fargate uses its default ECS backend and creates a cluster
        // for this service. Set aws-sample:clusterName to reuse an existing shared cluster.
        var sharedClusterName = new Config("aws-sample").Get("clusterName");
        AwsFargateOptions? fargateOptions = string.IsNullOrWhiteSpace(sharedClusterName)
            ? null
            : new AwsFargateOptions { ClusterName = sharedClusterName };

        var fargateService = factory.CreateCompute(
            "web-app",
            registry,
            containerImage: image,
            serviceType: ComputeServiceType.Fargate,
            providerOptions: fargateOptions
        );
        fargateService.PortForwardings =
        [
            new PortForwardingConfiguration { ContainerPort = 80 },
        ];

        // Add secret dependencies
        fargateService.AddRequiredSecret(secret);
        fargateService.Build();

        // Export some values - using public properties and Output types
        this.FargateUrl = fargateService.ServiceUrl;
        this.RegistryUrl = ((AwsContainerRegistry)registry).RegistryUrlOutput;
    }

    [Output]
    public Output<string> FargateUrl { get; set; }

    [Output]
    public Output<string> RegistryUrl { get; set; }
}
