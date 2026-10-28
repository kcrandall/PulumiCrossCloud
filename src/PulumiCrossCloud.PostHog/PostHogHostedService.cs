using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.PostHog;

public sealed class PostHogHostedService : ComponentResource
{
    public ICompute Compute { get; }
    public Output<string> ServiceUrl => Compute.ServiceUrl;

    public PostHogHostedService(CloudResourceFactory factory, string name, PostHogHostingArgs args)
        : base("posthog:HostedService", name, null, null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(args);

        var registry = args.Registry ?? factory.CreateContainerRegistry($"{name}-registry", false);
        var image = args.MirrorImageToRegistry
            ? PostHogImageMirror.Push(
                factory,
                registry,
                $"{name}-image",
                args.ImageName,
                args.ImageTag
            )
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
                Protocol = "TCP",
            },
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
            StartupUnhealthyThreshold = 30,
        };

        AddPostHogEnvironment(args);
        Compute.Build();

        RegisterOutputs(new Dictionary<string, object?> { ["serviceUrl"] = ServiceUrl });
    }

    private void AddPostHogEnvironment(PostHogHostingArgs args)
    {
        Compute.AddRequiredSecret(args.SecretKey, "SECRET_KEY");
        Compute.AddEnvironmentVariable("SITE_URL", args.SiteUrl);
        Compute.AddEnvironmentVariable("PORT", args.ContainerPort.ToString());
        Compute.AddEnvironmentVariable("IS_BEHIND_PROXY", args.IsBehindProxy.ToString());
        Compute.AddEnvironmentVariable("SECURE_COOKIES", args.SecureCookies.ToString());
        Compute.AddEnvironmentVariable("TRUST_ALL_PROXIES", args.TrustAllProxies.ToString());
        Compute.AddEnvironmentVariable(
            "DISABLE_SECURE_SSL_REDIRECT",
            args.DisableSecureSslRedirect.ToString()
        );
        Compute.AddEnvironmentVariable(
            "DISABLE_PAID_FEATURE_SHOWCASING",
            args.DisablePaidFeatureShowcasing.ToString()
        );

        AddOptionalSecret(args.JwtSigningKey, "JWT_SIGNING_KEY");
        AddOptionalSecret(args.EncryptionSaltKeys, "ENCRYPTION_SALT_KEYS");

        AddDatabaseEnvironment(args.Database);
        AddRedisEnvironment(args.Redis);
        AddClickHouseEnvironment(args.ClickHouse);
        AddOptionalEnvironment("KAFKA_HOSTS", args.KafkaHosts);
        AddOptionalEnvironment("KAFKA_URL", args.KafkaUrl);
        AddObjectStorageEnvironment(args.ObjectStorage);
        AddEmailEnvironment(args.Email);

        Compute.AddEnvironmentVariables(args.EnvironmentVariables);
        foreach (var (environmentVariableName, secret) in args.SecretEnvironmentVariables)
            Compute.AddRequiredSecret(secret, environmentVariableName);
    }

    private void AddDatabaseEnvironment(PostHogDatabaseInputs? database)
    {
        if (database is null)
            return;

        if (database.Url is not null)
        {
            Compute.AddRequiredSecret(database.Url, "DATABASE_URL");
            return;
        }

        Compute.AddEnvironmentVariable("POSTHOG_POSTGRES_HOST", database.Host);
        Compute.AddEnvironmentVariable("POSTHOG_POSTGRES_PORT", database.Port.ToString());
        Compute.AddEnvironmentVariable("POSTHOG_DB_NAME", database.Database);
        Compute.AddEnvironmentVariable("POSTHOG_DB_USER", database.Username);
        AddOptionalEnvironment("POSTHOG_POSTGRES_SSL_MODE", database.SslMode);
        AddOptionalSecret(database.Password, "POSTHOG_DB_PASSWORD");
    }

    private void AddRedisEnvironment(PostHogRedisInputs? redis)
    {
        if (redis is null)
            return;

        if (redis.Url is not null)
        {
            Compute.AddRequiredSecret(redis.Url, "REDIS_URL");
            return;
        }

        var url = $"redis://{redis.Host}:{redis.Port}/{redis.Database}";
        if (redis.Password is null)
        {
            Compute.AddEnvironmentVariable("REDIS_URL", url);
            return;
        }

        Compute.AddEnvironmentVariable(
            "REDIS_URL",
            redis.Password.Value.Apply(password =>
                string.IsNullOrWhiteSpace(password)
                    ? url
                    : $"redis://:{Uri.EscapeDataString(password)}@{redis.Host}:{redis.Port}/{redis.Database}"
            )
        );
    }

    private void AddClickHouseEnvironment(PostHogClickHouseInputs? clickHouse)
    {
        if (clickHouse is null)
            return;

        Compute.AddEnvironmentVariable("CLICKHOUSE_HOST", clickHouse.Host);
        Compute.AddEnvironmentVariable("CLICKHOUSE_PORT", clickHouse.Port.ToString());
        Compute.AddEnvironmentVariable("CLICKHOUSE_DATABASE", clickHouse.Database);
        Compute.AddEnvironmentVariable(
            "CLICKHOUSE_SECURE",
            clickHouse.Secure.ToString().ToLowerInvariant()
        );
        Compute.AddEnvironmentVariable(
            "CLICKHOUSE_VERIFY",
            clickHouse.Verify.ToString().ToLowerInvariant()
        );
        Compute.AddEnvironmentVariable(
            "CLICKHOUSE_DISABLE_EXTERNAL_SCHEMAS",
            clickHouse.DisableExternalSchemas.ToString()
        );
        AddOptionalEnvironment("CLICKHOUSE_USER", clickHouse.Username);
        AddOptionalSecret(clickHouse.Password, "CLICKHOUSE_PASSWORD");
    }

    private void AddObjectStorageEnvironment(PostHogObjectStorageInputs? objectStorage)
    {
        if (objectStorage is null)
            return;

        Compute.AddEnvironmentVariable("OBJECT_STORAGE_ENABLED", "true");
        Compute.AddEnvironmentVariable("OBJECT_STORAGE_BUCKET", objectStorage.Bucket);
        AddOptionalEnvironment("OBJECT_STORAGE_ENDPOINT", objectStorage.Endpoint);
        AddOptionalEnvironment("OBJECT_STORAGE_PUBLIC_ENDPOINT", objectStorage.PublicEndpoint);
        AddOptionalEnvironment("OBJECT_STORAGE_REGION", objectStorage.Region);
        AddOptionalEnvironment("OBJECT_STORAGE_ACCESS_KEY_ID", objectStorage.AccessKeyId);
        AddOptionalSecret(objectStorage.SecretAccessKey, "OBJECT_STORAGE_SECRET_ACCESS_KEY");
    }

    private void AddEmailEnvironment(PostHogEmailInputs? email)
    {
        if (email is null)
            return;

        Compute.AddEnvironmentVariable("EMAIL_ENABLED", email.Enabled.ToString());
        Compute.AddEnvironmentVariable("EMAIL_HOST", email.Host);
        Compute.AddEnvironmentVariable("EMAIL_PORT", email.Port.ToString());
        Compute.AddEnvironmentVariable("EMAIL_USE_TLS", email.UseTls.ToString());
        Compute.AddEnvironmentVariable("EMAIL_USE_SSL", email.UseSsl.ToString());
        AddOptionalEnvironment("EMAIL_HOST_USER", email.User);
        AddOptionalEnvironment("EMAIL_DEFAULT_FROM", email.DefaultFrom);
        AddOptionalSecret(email.Password, "EMAIL_HOST_PASSWORD");
    }

    private void AddOptionalEnvironment(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Compute.AddEnvironmentVariable(name, value);
    }

    private void AddOptionalSecret(ISecret? secret, string environmentVariableName)
    {
        if (secret is not null)
            Compute.AddRequiredSecret(secret, environmentVariableName);
    }
}
