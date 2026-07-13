using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.PostHog;

public sealed class PostHogManagedHostingArgs
{
    public required PostHogHostingArgs Hosting { get; init; }
    public bool CreatePostgres { get; init; } = true;
    public IPostgresOptions? PostgresOptions { get; init; }
    public bool CreateRedis { get; init; } = true;
    public MemoryCacheOptions? RedisOptions { get; init; }
}

public sealed class PostHogManagedHostedService : ComponentResource
{
    public PostHogHostedService PostHog { get; }
    public IPostgresDatabase? Postgres { get; }
    public IMemoryCacheDatabase? Redis { get; }
    public ICompute Compute => PostHog.Compute;
    public Output<string> ServiceUrl => PostHog.ServiceUrl;

    public PostHogManagedHostedService(
        CloudResourceFactory factory,
        string name,
        PostHogManagedHostingArgs args
    )
        : base("posthog:ManagedHostedService", name, null, null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(args);

        var databaseInputs = args.Hosting.Database;
        if (databaseInputs is null && args.CreatePostgres)
        {
            Postgres = factory.CreatePostgresDatabase(
                $"{name}-postgres",
                args.PostgresOptions
                    ?? new PostgresOptions
                    {
                        InitialDatabaseName = "posthog",
                        MasterUsername = "posthog",
                    }
            );
            databaseInputs = new PostHogDatabaseInputs
            {
                Url = factory.CreateSecret($"{name}-postgres-url", Postgres.ConnectionString),
            };
        }

        var redisInputs = args.Hosting.Redis;
        if (redisInputs is null && args.CreateRedis)
        {
            Redis = factory.CreateMemoryCache($"{name}-redis", args.RedisOptions);
            redisInputs = new PostHogRedisInputs
            {
                Url = factory.CreateSecret($"{name}-redis-url", Redis.ConnectionString),
            };
        }

        PostHog = new PostHogHostedService(
            factory,
            name,
            WithDependencies(args.Hosting, databaseInputs, redisInputs)
        );

        RegisterOutputs(new Dictionary<string, object?> { ["serviceUrl"] = ServiceUrl });
    }

    private static PostHogHostingArgs WithDependencies(
        PostHogHostingArgs args,
        PostHogDatabaseInputs? database,
        PostHogRedisInputs? redis
    ) =>
        new()
        {
            SecretKey = args.SecretKey,
            SiteUrl = args.SiteUrl,
            JwtSigningKey = args.JwtSigningKey,
            EncryptionSaltKeys = args.EncryptionSaltKeys,
            Database = database,
            Redis = redis,
            ClickHouse = args.ClickHouse,
            KafkaHosts = args.KafkaHosts,
            KafkaUrl = args.KafkaUrl,
            ObjectStorage = args.ObjectStorage,
            Email = args.Email,
            Registry = args.Registry,
            ImageName = args.ImageName,
            ImageTag = args.ImageTag,
            IsBehindProxy = args.IsBehindProxy,
            SecureCookies = args.SecureCookies,
            TrustAllProxies = args.TrustAllProxies,
            DisableSecureSslRedirect = args.DisableSecureSslRedirect,
            DisablePaidFeatureShowcasing = args.DisablePaidFeatureShowcasing,
            EnvironmentVariables = args.EnvironmentVariables,
            SecretEnvironmentVariables = args.SecretEnvironmentVariables,
            ServiceType = args.ServiceType,
            ContainerPort = args.ContainerPort,
            MinCount = args.MinCount,
            MaxCount = args.MaxCount,
            MemoryLimit = args.MemoryLimit,
            CpuLimit = args.CpuLimit,
            MirrorImageToRegistry = args.MirrorImageToRegistry,
        };
}
