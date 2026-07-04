using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Grafana;

public sealed class GrafanaManagedHostingArgs
{
    public required GrafanaHostingArgs Hosting { get; init; }
    public bool CreatePostgres { get; init; } = true;
    public IPostgresOptions? PostgresOptions { get; init; }
    public bool CreateRedis { get; init; } = true;
    public MemoryCacheOptions? RedisOptions { get; init; }
}

public sealed class GrafanaManagedHostedService : ComponentResource
{
    public GrafanaHostedService Grafana { get; }
    public IPostgresDatabase? Postgres { get; }
    public IMemoryCacheDatabase? Redis { get; }
    public ICompute Compute => Grafana.Compute;
    public Output<string> ServiceUrl => Grafana.ServiceUrl;

    public GrafanaManagedHostedService(
        CloudResourceFactory factory,
        string name,
        GrafanaManagedHostingArgs args
    )
        : base("grafana:ManagedHostedService", name, null, null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(args);

        var postgresInputs = args.Hosting.Database;
        if (postgresInputs is null && args.CreatePostgres)
        {
            Postgres = factory.CreatePostgresDatabase(
                $"{name}-postgres",
                args.PostgresOptions ?? new PostgresOptions
                {
                    InitialDatabaseName = "grafana",
                    MasterUsername = "grafana"
                }
            );
            postgresInputs = new GrafanaDatabaseInputs
            {
                Url = factory.CreateSecret($"{name}-postgres-url", Postgres.ConnectionString)
            };
        }

        var redisInputs = args.Hosting.Redis;
        if (redisInputs is null && args.CreateRedis)
        {
            Redis = factory.CreateMemoryCache($"{name}-redis", args.RedisOptions);
            redisInputs = new GrafanaRedisInputs
            {
                RemoteCacheConnectionString = factory.CreateSecret(
                    $"{name}-redis-connstr",
                    BuildRemoteCacheConnectionString(Redis)
                ),
                EnableAlertingHighAvailability = false
            };
        }

        Grafana = new GrafanaHostedService(
            factory,
            name,
            WithDependencies(args.Hosting, postgresInputs, redisInputs)
        );

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["serviceUrl"] = ServiceUrl
        });
    }

    private static Output<string> BuildRemoteCacheConnectionString(IMemoryCacheDatabase redis) =>
        Output.Tuple(redis.Host, redis.Port, redis.Username, redis.Password)
            .Apply(values =>
            {
                var connectionString = $"addr={values.Item1}:{values.Item2}";
                if (!string.IsNullOrWhiteSpace(values.Item3))
                {
                    connectionString += $",username={values.Item3}";
                }

                if (!string.IsNullOrWhiteSpace(values.Item4))
                {
                    connectionString += $",password={values.Item4}";
                }

                return connectionString;
            });

    private static GrafanaHostingArgs WithDependencies(
        GrafanaHostingArgs args,
        GrafanaDatabaseInputs? database,
        GrafanaRedisInputs? redis
    ) =>
        new()
        {
            AdminPassword = args.AdminPassword,
            SecretKey = args.SecretKey,
            AdminUser = args.AdminUser,
            Database = database,
            Redis = redis,
            Smtp = args.Smtp,
            Registry = args.Registry,
            ImageName = args.ImageName,
            ImageTag = args.ImageTag,
            RootUrl = args.RootUrl,
            Domain = args.Domain,
            ServeFromSubPath = args.ServeFromSubPath,
            AnonymousAccessEnabled = args.AnonymousAccessEnabled,
            AnonymousOrgRoleViewer = args.AnonymousOrgRoleViewer,
            Plugins = args.Plugins,
            EnvironmentVariables = args.EnvironmentVariables,
            SecretEnvironmentVariables = args.SecretEnvironmentVariables,
            ServiceType = args.ServiceType,
            ContainerPort = args.ContainerPort,
            MinCount = args.MinCount,
            MaxCount = args.MaxCount,
            MemoryLimit = args.MemoryLimit,
            CpuLimit = args.CpuLimit,
            MirrorImageToRegistry = args.MirrorImageToRegistry
        };
}
