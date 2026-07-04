using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Sentry;

public sealed class SentryRelayService : ComponentResource
{
    public ICompute Compute { get; }
    public Output<string> ServiceUrl => Compute.ServiceUrl;

    public SentryRelayService(
        CloudResourceFactory factory,
        string name,
        SentryRelayHostingArgs args
    )
        : base("sentry:RelayService", name, null, null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(args);

        var registry = args.Registry ?? factory.CreateContainerRegistry($"{name}-registry", false);
        var image = args.MirrorImageToRegistry
            ? SentryImageMirror.Push(factory, registry, $"{name}-image", args.ImageName, args.ImageTag)
            : null;

        Compute = factory.CreateCompute(
            name,
            registry,
            imageName: image is null ? args.ImageName : null,
            imageTag: args.ImageTag,
            containerImage: image,
            serviceType: args.ServiceType
        );

        Compute.MemoryLimit = args.MemoryLimit;
        Compute.CpuLimit = args.CpuLimit;
        Compute.MinCount = args.MinCount;
        Compute.MaxCount = args.MaxCount;
        Compute.PortForwardings =
        [
            new PortForwardingConfiguration
            {
                ContainerPort = args.Port,
                ExternalPort = args.Port,
                Protocol = "TCP"
            }
        ];
        Compute.HealthCheckConfiguration = new HealthCheckConfiguration
        {
            Path = "/api/relay/healthcheck/live/",
            IntervalSeconds = 30,
            TimeoutSeconds = 5,
            HealthyThreshold = 2,
            UnhealthyThreshold = 3,
            StartupIntervalSeconds = 10,
            StartupTimeoutSeconds = 5,
            StartupUnhealthyThreshold = 18
        };

        Compute.AddEnvironmentVariable("RELAY_MODE", args.Mode);
        Compute.AddEnvironmentVariable("RELAY_UPSTREAM_URL", args.UpstreamUrl);
        Compute.AddEnvironmentVariable("RELAY_HOST", args.Host);
        Compute.AddEnvironmentVariable("RELAY_PORT", args.Port.ToString());

        if (!string.IsNullOrWhiteSpace(args.RelayId))
        {
            Compute.AddEnvironmentVariable("RELAY_ID", args.RelayId);
        }

        if (!string.IsNullOrWhiteSpace(args.PublicKey))
        {
            Compute.AddEnvironmentVariable("RELAY_PUBLIC_KEY", args.PublicKey);
        }

        if (args.SecretKey is not null)
        {
            Compute.AddRequiredSecret(args.SecretKey, "RELAY_SECRET_KEY");
        }

        Compute.AddEnvironmentVariables(args.EnvironmentVariables);
        foreach (var (environmentVariableName, secret) in args.SecretEnvironmentVariables)
        {
            Compute.AddRequiredSecret(secret, environmentVariableName);
        }

        Compute.Build();

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["serviceUrl"] = ServiceUrl
        });
    }
}
