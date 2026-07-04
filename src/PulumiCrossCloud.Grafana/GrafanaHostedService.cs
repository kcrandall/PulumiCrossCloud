using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Grafana;

public sealed class GrafanaHostedService : ComponentResource
{
    public ICompute Compute { get; }
    public Output<string> ServiceUrl => Compute.ServiceUrl;

    public GrafanaHostedService(
        CloudResourceFactory factory,
        string name,
        GrafanaHostingArgs args
    )
        : base("grafana:HostedService", name, null, null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(args);

        var registry = args.Registry ?? factory.CreateContainerRegistry($"{name}-registry", false);
        var image = args.MirrorImageToRegistry
            ? GrafanaImageMirror.Push(factory, registry, $"{name}-image", args.ImageName, args.ImageTag)
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
            Path = "/api/health",
            IntervalSeconds = 30,
            TimeoutSeconds = 5,
            HealthyThreshold = 2,
            UnhealthyThreshold = 3,
            StartupIntervalSeconds = 10,
            StartupTimeoutSeconds = 5,
            StartupUnhealthyThreshold = 18
        };

        AddGrafanaEnvironment(args);
        Compute.Build();

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["serviceUrl"] = ServiceUrl
        });
    }

    private void AddGrafanaEnvironment(GrafanaHostingArgs args)
    {
        Compute.AddEnvironmentVariable("GF_SERVER_HTTP_PORT", args.ContainerPort.ToString());
        Compute.AddEnvironmentVariable("GF_SECURITY_ADMIN_USER", args.AdminUser);
        Compute.AddRequiredSecret(args.AdminPassword, "GF_SECURITY_ADMIN_PASSWORD");

        if (args.SecretKey is not null)
        {
            Compute.AddRequiredSecret(args.SecretKey, "GF_SECURITY_SECRET_KEY");
        }

        AddOptionalEnvironment("GF_SERVER_ROOT_URL", args.RootUrl);
        AddOptionalEnvironment("GF_SERVER_DOMAIN", args.Domain);

        if (args.ServeFromSubPath)
        {
            Compute.AddEnvironmentVariable("GF_SERVER_SERVE_FROM_SUB_PATH", "true");
        }

        if (args.AnonymousAccessEnabled)
        {
            Compute.AddEnvironmentVariable("GF_AUTH_ANONYMOUS_ENABLED", "true");
            Compute.AddEnvironmentVariable(
                "GF_AUTH_ANONYMOUS_ORG_ROLE",
                args.AnonymousOrgRoleViewer ? "Viewer" : "Editor"
            );
        }

        if (args.Plugins.Count > 0)
        {
            Compute.AddEnvironmentVariable("GF_INSTALL_PLUGINS", string.Join(",", args.Plugins));
        }

        AddDatabaseEnvironment(args.Database);
        AddRedisEnvironment(args.Redis);
        AddSmtpEnvironment(args.Smtp);

        Compute.AddEnvironmentVariables(args.EnvironmentVariables);
        foreach (var (environmentVariableName, secret) in args.SecretEnvironmentVariables)
        {
            Compute.AddRequiredSecret(secret, environmentVariableName);
        }
    }

    private void AddDatabaseEnvironment(GrafanaDatabaseInputs? database)
    {
        if (database is null)
        {
            return;
        }

        Compute.AddEnvironmentVariable("GF_DATABASE_TYPE", database.Type);

        if (database.Url is not null)
        {
            Compute.AddRequiredSecret(database.Url, "GF_DATABASE_URL");
            return;
        }

        Compute.AddEnvironmentVariable("GF_DATABASE_HOST", $"{database.Host}:{database.Port}");
        Compute.AddEnvironmentVariable("GF_DATABASE_NAME", database.Database);
        Compute.AddEnvironmentVariable("GF_DATABASE_USER", database.Username);
        Compute.AddEnvironmentVariable("GF_DATABASE_SSL_MODE", database.SslMode);

        if (database.Password is not null)
        {
            Compute.AddRequiredSecret(database.Password, "GF_DATABASE_PASSWORD");
        }
    }

    private void AddRedisEnvironment(GrafanaRedisInputs? redis)
    {
        if (redis is null)
        {
            return;
        }

        if (redis.EnableRemoteCache)
        {
            Compute.AddEnvironmentVariable("GF_REMOTE_CACHE_TYPE", "redis");
            if (redis.RemoteCacheConnectionString is not null)
            {
                Compute.AddRequiredSecret(redis.RemoteCacheConnectionString, "GF_REMOTE_CACHE_CONNSTR");
            }
            else
            {
                AddRemoteCacheConnectionString(redis);
            }
        }

        if (redis.EnableAlertingHighAvailability)
        {
            Compute.AddEnvironmentVariable("GF_UNIFIED_ALERTING_HA_REDIS_ADDRESS", redis.Address);
            AddOptionalEnvironment("GF_UNIFIED_ALERTING_HA_REDIS_USERNAME", redis.Username);
            Compute.AddEnvironmentVariable(
                "GF_UNIFIED_ALERTING_HA_REDIS_CLUSTER_MODE_ENABLED",
                redis.ClusterModeEnabled.ToString().ToLowerInvariant()
            );
            Compute.AddEnvironmentVariable(
                "GF_UNIFIED_ALERTING_HA_REDIS_SENTINEL_MODE_ENABLED",
                redis.SentinelModeEnabled.ToString().ToLowerInvariant()
            );
            AddOptionalEnvironment(
                "GF_UNIFIED_ALERTING_HA_REDIS_SENTINEL_MASTER_NAME",
                redis.SentinelMasterName
            );
        }

        if (redis.Password is not null)
        {
            Compute.AddRequiredSecret(redis.Password, "GF_UNIFIED_ALERTING_HA_REDIS_PASSWORD");
        }
    }

    private void AddSmtpEnvironment(GrafanaSmtpInputs? smtp)
    {
        if (smtp is null)
        {
            return;
        }

        Compute.AddEnvironmentVariable("GF_SMTP_ENABLED", smtp.Enabled.ToString().ToLowerInvariant());
        Compute.AddEnvironmentVariable("GF_SMTP_HOST", smtp.Host);
        Compute.AddEnvironmentVariable("GF_SMTP_SKIP_VERIFY", smtp.SkipVerify.ToString().ToLowerInvariant());
        AddOptionalEnvironment("GF_SMTP_USER", smtp.User);
        AddOptionalEnvironment("GF_SMTP_FROM_ADDRESS", smtp.FromAddress);
        AddOptionalEnvironment("GF_SMTP_FROM_NAME", smtp.FromName);

        if (smtp.Password is not null)
        {
            Compute.AddRequiredSecret(smtp.Password, "GF_SMTP_PASSWORD");
        }
    }

    private static string BuildRedisConnectionString(GrafanaRedisInputs redis)
    {
        var connectionString = $"addr={redis.Address}";
        if (!string.IsNullOrWhiteSpace(redis.Username))
        {
            connectionString += $",username={redis.Username}";
        }

        return connectionString;
    }

    private void AddRemoteCacheConnectionString(GrafanaRedisInputs redis)
    {
        var connectionString = BuildRedisConnectionString(redis);
        if (redis.Password is null)
        {
            Compute.AddEnvironmentVariable("GF_REMOTE_CACHE_CONNSTR", connectionString);
            return;
        }

        Compute.AddEnvironmentVariable(
            "GF_REMOTE_CACHE_CONNSTR",
            redis.Password.Value.Apply(password =>
                string.IsNullOrWhiteSpace(password)
                    ? connectionString
                    : $"{connectionString},password={password}"
            )
        );
    }

    private void AddOptionalEnvironment(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Compute.AddEnvironmentVariable(name, value);
        }
    }
}
