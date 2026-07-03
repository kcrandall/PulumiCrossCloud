using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Extensions;

public static class ComputeServiceTypeExtensions
{
    /// <summary>
    /// Gets a human-readable display name for the compute service type
    /// </summary>
    public static string GetDisplayName(this ComputeServiceType serviceType)
    {
        return serviceType switch
        {
            ComputeServiceType.AppRunner => "AWS App Runner",
            ComputeServiceType.Fargate => "AWS Fargate",
            ComputeServiceType.EC2 => "Amazon EC2",
            ComputeServiceType.ECS => "Amazon ECS",
            ComputeServiceType.ContainerApps => "Azure Container Apps",
            ComputeServiceType.ContainerInstances => "Azure Container Instances",
            ComputeServiceType.AppService => "Azure App Service",
            ComputeServiceType.CloudRun => "Google Cloud Run",
            ComputeServiceType.CloudRunJobs => "Google Cloud Run Jobs",
            ComputeServiceType.ComputeEngine => "Google Compute Engine",
            ComputeServiceType.KoyebService => "Koyeb Service",
            ComputeServiceType.FlyMachine => "Fly.io Machine",
            _ => serviceType.ToString()
        };
    }
    
    /// <summary>
    /// Gets the cloud provider for the compute service type
    /// </summary>
    public static CloudProvider GetCloudProvider(this ComputeServiceType serviceType)
    {
        return serviceType switch
        {
            ComputeServiceType.AppRunner => CloudProvider.AWS,
            ComputeServiceType.Fargate => CloudProvider.AWS,
            ComputeServiceType.EC2 => CloudProvider.AWS,
            ComputeServiceType.ECS => CloudProvider.AWS,
            ComputeServiceType.ContainerApps => CloudProvider.Azure,
            ComputeServiceType.ContainerInstances => CloudProvider.Azure,
            ComputeServiceType.AppService => CloudProvider.Azure,
            ComputeServiceType.CloudRun => CloudProvider.GCP,
            ComputeServiceType.CloudRunJobs => CloudProvider.GCP,
            ComputeServiceType.ComputeEngine => CloudProvider.GCP,
            ComputeServiceType.KoyebService => CloudProvider.Koyeb,
            ComputeServiceType.FlyMachine => CloudProvider.Fly,
            _ => throw new ArgumentException($"Unknown service type: {serviceType}")
        };
    }
    
    /// <summary>
    /// Checks if the service type is serverless
    /// </summary>
    public static bool IsServerless(this ComputeServiceType serviceType)
    {
        return serviceType switch
        {
            ComputeServiceType.AppRunner => true,
            ComputeServiceType.Fargate => true,
            ComputeServiceType.ContainerApps => true,
            ComputeServiceType.ContainerInstances => true,
            ComputeServiceType.CloudRun => true,
            ComputeServiceType.CloudRunJobs => true,
            ComputeServiceType.KoyebService => true,
            ComputeServiceType.FlyMachine => true,
            ComputeServiceType.EC2 => false,
            ComputeServiceType.ECS => false,
            ComputeServiceType.AppService => false,
            ComputeServiceType.ComputeEngine => false,
            _ => false
        };
    }
    
    /// <summary>
    /// Checks if the service type supports containers
    /// </summary>
    public static bool SupportsContainers(this ComputeServiceType serviceType)
    {
        return serviceType switch
        {
            ComputeServiceType.AppRunner => true,
            ComputeServiceType.Fargate => true,
            ComputeServiceType.ECS => true,
            ComputeServiceType.ContainerApps => true,
            ComputeServiceType.ContainerInstances => true,
            ComputeServiceType.CloudRun => true,
            ComputeServiceType.CloudRunJobs => true,
            ComputeServiceType.KoyebService => true,
            ComputeServiceType.FlyMachine => true,
            ComputeServiceType.EC2 => false,
            ComputeServiceType.AppService => false,
            ComputeServiceType.ComputeEngine => false,
            _ => false
        };
    }
    
    /// <summary>
    /// Gets the typical use cases for the service type
    /// </summary>
    public static IEnumerable<string> GetUseCases(this ComputeServiceType serviceType)
    {
        return serviceType switch
        {
            ComputeServiceType.AppRunner => new[] { "Web APIs", "Simple containerized apps", "CI/CD deployments" },
            ComputeServiceType.Fargate => new[] { "Microservices", "Batch processing", "Background workers" },
            ComputeServiceType.EC2 => new[] { "Full control applications", "Legacy workloads", "Custom configurations" },
            ComputeServiceType.ECS => new[] { "Container orchestration", "Multi-container apps", "Service mesh" },
            ComputeServiceType.ContainerApps => new[] { "Event-driven apps", "Microservices", "Background processing" },
            ComputeServiceType.ContainerInstances => new[] { "Batch jobs", "Simple containers", "Dev/test workloads" },
            ComputeServiceType.AppService => new[] { "Web apps", "APIs", "Mobile backends" },
            ComputeServiceType.CloudRun => new[] { "HTTP services", "Event processing", "Microservices" },
            ComputeServiceType.CloudRunJobs => new[] { "Batch processing", "Scheduled tasks", "Data processing" },
            ComputeServiceType.ComputeEngine => new[] { "VM workloads", "Legacy applications", "Custom OS requirements" },
            ComputeServiceType.KoyebService => new[] { "Web services", "APIs", "Background workers" },
            ComputeServiceType.FlyMachine => new[] { "Global apps", "Low-latency APIs", "Stateful services" },
            _ => new[] { "General purpose computing" }
        };
    }
    
    /// <summary>
    /// Gets the billing model for the service type
    /// </summary>
    public static string GetBillingModel(this ComputeServiceType serviceType)
    {
        return serviceType switch
        {
            ComputeServiceType.AppRunner => "Pay per request + provisioned capacity",
            ComputeServiceType.Fargate => "Pay per vCPU and memory per second",
            ComputeServiceType.EC2 => "Pay per instance hour",
            ComputeServiceType.ECS => "Pay for underlying EC2/Fargate resources",
            ComputeServiceType.ContainerApps => "Pay per vCPU and memory consumption",
            ComputeServiceType.ContainerInstances => "Pay per vCPU and memory per second",
            ComputeServiceType.AppService => "Pay per App Service Plan",
            ComputeServiceType.CloudRun => "Pay per request + CPU/memory allocation",
            ComputeServiceType.CloudRunJobs => "Pay per job execution time",
            ComputeServiceType.ComputeEngine => "Pay per VM hour",
            ComputeServiceType.KoyebService => "Pay per instance type and usage",
            ComputeServiceType.FlyMachine => "Pay per machine resources and usage",
            _ => "Variable billing model"
        };
    }
}
