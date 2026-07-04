using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Sentry;

public sealed class SentryHostedService : ComponentResource
{
    public ICompute Compute { get; }
    public Output<string> ServiceUrl => Compute.ServiceUrl;

    public SentryHostedService(
        CloudResourceFactory factory,
        string name,
        SentryHostingArgs args
    )
        : base("sentry:HostedService", name, null, null)
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
                ContainerPort = args.ContainerPort,
                ExternalPort = args.ContainerPort,
                Protocol = "TCP"
            }
        ];
        Compute.HealthCheckConfiguration = new HealthCheckConfiguration
        {
            Path = "/_health/",
            IntervalSeconds = 30,
            TimeoutSeconds = 10,
            HealthyThreshold = 2,
            UnhealthyThreshold = 3,
            StartupIntervalSeconds = 30,
            StartupTimeoutSeconds = 10,
            StartupUnhealthyThreshold = 20
        };

        AddSentryEnvironment(args);
        Compute.Build();

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["serviceUrl"] = ServiceUrl
        });
    }

    private void AddSentryEnvironment(SentryHostingArgs args)
    {
        var postgres = args.Dependencies.Postgres;
        var redis = args.Dependencies.Redis;

        Compute.AddRequiredSecret(args.SecretKey, "SENTRY_SECRET_KEY");

        Compute.AddEnvironmentVariable("SENTRY_POSTGRES_HOST", postgres.Host);
        Compute.AddEnvironmentVariable("SENTRY_POSTGRES_PORT", postgres.Port.ToString());
        Compute.AddEnvironmentVariable("SENTRY_POSTGRES_NAME", postgres.Database);
        Compute.AddEnvironmentVariable("SENTRY_POSTGRES_USER", postgres.Username);
        Compute.AddRequiredSecret(postgres.Password, "SENTRY_POSTGRES_PASSWORD");

        Compute.AddEnvironmentVariable("SENTRY_DB_NAME", postgres.Database);
        Compute.AddEnvironmentVariable("SENTRY_DB_USER", postgres.Username);
        Compute.AddRequiredSecret(postgres.Password, "SENTRY_DB_PASSWORD");

        if (postgres.ConnectionString is not null)
        {
            Compute.AddRequiredSecret(postgres.ConnectionString, "SENTRY_DATABASE_URL");
        }

        Compute.AddEnvironmentVariable("SENTRY_REDIS_HOST", redis.Host);
        Compute.AddEnvironmentVariable("SENTRY_REDIS_PORT", redis.Port.ToString());
        Compute.AddEnvironmentVariable("SENTRY_REDIS_DB", redis.Database.ToString());

        if (redis.Password is not null)
        {
            Compute.AddRequiredSecret(redis.Password, "SENTRY_REDIS_PASSWORD");
        }

        if (redis.Url is not null)
        {
            Compute.AddRequiredSecret(redis.Url, "SENTRY_REDIS_URL");
        }

        if (!string.IsNullOrWhiteSpace(args.SystemUrlPrefix))
        {
            Compute.AddEnvironmentVariable("SENTRY_SYSTEM_URL_PREFIX", args.SystemUrlPrefix);
        }

        AddOptionalEnvironment("SENTRY_KAFKA_BOOTSTRAP_SERVERS", args.Dependencies.KafkaBootstrapServers);
        AddOptionalEnvironment("SENTRY_CLICKHOUSE_HOST", args.Dependencies.ClickHouseHost);
        AddOptionalEnvironment(
            "SENTRY_CLICKHOUSE_PORT",
            args.Dependencies.ClickHousePort?.ToString()
        );
        AddOptionalEnvironment("SENTRY_SNUBA", args.Dependencies.SnubaUrl);
        AddOptionalEnvironment("SENTRY_MEMCACHED_SERVERS", args.Dependencies.MemcachedServers);

        Compute.AddEnvironmentVariables(args.EnvironmentVariables);
        foreach (var (environmentVariableName, secret) in args.SecretEnvironmentVariables)
        {
            Compute.AddRequiredSecret(secret, environmentVariableName);
        }
    }

    private void AddOptionalEnvironment(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Compute.AddEnvironmentVariable(name, value);
        }
    }
}
