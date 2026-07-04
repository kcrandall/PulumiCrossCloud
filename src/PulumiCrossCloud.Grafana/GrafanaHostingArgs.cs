using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Grafana;

public sealed class GrafanaDatabaseInputs
{
    public string Type { get; init; } = "postgres";
    public string Host { get; init; } = "postgres";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "grafana";
    public string Username { get; init; } = "grafana";
    public ISecret? Password { get; init; }
    public string SslMode { get; init; } = "require";
    public ISecret? Url { get; init; }
}

public sealed class GrafanaRedisInputs
{
    public string Address { get; init; } = "redis:6379";
    public string? Username { get; init; }
    public ISecret? Password { get; init; }
    public ISecret? RemoteCacheConnectionString { get; init; }
    public bool EnableRemoteCache { get; init; } = true;
    public bool EnableAlertingHighAvailability { get; init; } = true;
    public bool ClusterModeEnabled { get; init; }
    public bool SentinelModeEnabled { get; init; }
    public string? SentinelMasterName { get; init; }
}

public sealed class GrafanaSmtpInputs
{
    public string Host { get; init; } = "smtp:587";
    public string? User { get; init; }
    public ISecret? Password { get; init; }
    public string? FromAddress { get; init; }
    public string? FromName { get; init; }
    public bool Enabled { get; init; } = true;
    public bool SkipVerify { get; init; }
}

public sealed class GrafanaHostingArgs
{
    public required ISecret AdminPassword { get; init; }
    public ISecret? SecretKey { get; init; }
    public string AdminUser { get; init; } = "admin";
    public GrafanaDatabaseInputs? Database { get; init; }
    public GrafanaRedisInputs? Redis { get; init; }
    public GrafanaSmtpInputs? Smtp { get; init; }
    public IContainerRegistry? Registry { get; init; }
    public string ImageName { get; init; } = "grafana/grafana";
    public string ImageTag { get; init; } = "latest";
    public string? RootUrl { get; init; }
    public string? Domain { get; init; }
    public bool ServeFromSubPath { get; init; }
    public bool AnonymousAccessEnabled { get; init; }
    public bool AnonymousOrgRoleViewer { get; init; } = true;
    public IReadOnlyList<string> Plugins { get; init; } = [];
    public IDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>();
    public IDictionary<string, ISecret> SecretEnvironmentVariables { get; init; } =
        new Dictionary<string, ISecret>();
    public ComputeServiceType? ServiceType { get; init; }
    public int ContainerPort { get; init; } = 3000;
    public int MinCount { get; init; } = 1;
    public int MaxCount { get; init; } = 1;
    public string MemoryLimit { get; init; } = "1Gi";
    public string CpuLimit { get; init; } = "1";
    public bool MirrorImageToRegistry { get; init; } = true;
}
