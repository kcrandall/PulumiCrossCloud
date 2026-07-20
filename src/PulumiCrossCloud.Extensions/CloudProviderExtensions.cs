using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Extensions;

public static class CloudProviderExtensions
{
    /// <summary>
    /// Gets a human-readable display name for the cloud provider
    /// </summary>
    public static string GetDisplayName(this CloudProvider provider)
    {
        return provider switch
        {
            CloudProvider.AWS => "Amazon Web Services",
            CloudProvider.Azure => "Microsoft Azure",
            CloudProvider.GCP => "Google Cloud Platform",
            CloudProvider.Koyeb => "Koyeb",
            CloudProvider.Fly => "Fly.io",
            CloudProvider.DigitalOcean => "DigitalOcean",
            _ => provider.ToString(),
        };
    }

    /// <summary>
    /// Gets the short name/abbreviation for the cloud provider
    /// </summary>
    public static string GetShortName(this CloudProvider provider)
    {
        return provider switch
        {
            CloudProvider.AWS => "AWS",
            CloudProvider.Azure => "Azure",
            CloudProvider.GCP => "GCP",
            CloudProvider.Koyeb => "Koyeb",
            CloudProvider.Fly => "Fly.io",
            CloudProvider.DigitalOcean => "DigitalOcean",
            _ => provider.ToString(),
        };
    }

    /// <summary>
    /// Gets supported compute service types for the provider
    /// </summary>
    public static IEnumerable<ComputeServiceType> GetSupportedComputeTypes(
        this CloudProvider provider
    )
    {
        return provider switch
        {
            CloudProvider.AWS => new[]
            {
                ComputeServiceType.Fargate,
                ComputeServiceType.EC2,
                ComputeServiceType.ECS,
            },
            CloudProvider.Azure => new[]
            {
                ComputeServiceType.ContainerApps,
                ComputeServiceType.ContainerInstances,
                ComputeServiceType.AppService,
            },
            CloudProvider.GCP => new[]
            {
                ComputeServiceType.CloudRun,
                ComputeServiceType.CloudRunJobs,
                ComputeServiceType.ComputeEngine,
            },
            CloudProvider.Koyeb => new[] { ComputeServiceType.KoyebService },
            CloudProvider.Fly => new[] { ComputeServiceType.FlyMachine },
            CloudProvider.DigitalOcean => new[] { ComputeServiceType.DigitalOceanAppPlatform },
            _ => Enumerable.Empty<ComputeServiceType>(),
        };
    }

    /// <summary>
    /// Checks if a compute service type is supported by the provider
    /// </summary>
    public static bool SupportsComputeType(
        this CloudProvider provider,
        ComputeServiceType serviceType
    )
    {
        return provider.GetSupportedComputeTypes().Contains(serviceType);
    }

    /// <summary>
    /// Gets the default compute service type for the provider
    /// </summary>
    public static ComputeServiceType GetDefaultComputeType(this CloudProvider provider)
    {
        return provider switch
        {
            CloudProvider.AWS => ComputeServiceType.Fargate,
            CloudProvider.Azure => ComputeServiceType.ContainerApps,
            CloudProvider.GCP => ComputeServiceType.CloudRun,
            CloudProvider.Koyeb => ComputeServiceType.KoyebService,
            CloudProvider.Fly => ComputeServiceType.FlyMachine,
            CloudProvider.DigitalOcean => ComputeServiceType.DigitalOceanAppPlatform,
            _ => throw new ArgumentException($"No default compute type for provider: {provider}"),
        };
    }

    /// <summary>
    /// Gets the resource identifier format for the provider
    /// </summary>
    public static string GetResourceIdFormat(this CloudProvider provider)
    {
        return provider switch
        {
            CloudProvider.AWS => "arn:aws:service:region:account:resource",
            CloudProvider.Azure =>
                "/subscriptions/sub/resourceGroups/rg/providers/provider/resource",
            CloudProvider.GCP => "projects/project/locations/location/resource",
            CloudProvider.Koyeb => "koyeb://resource/id",
            CloudProvider.Fly => "fly://app/resource/id",
            CloudProvider.DigitalOcean => "do://resource/id",
            _ => "unknown",
        };
    }
}
