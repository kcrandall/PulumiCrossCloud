using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Sentry;

public sealed class SentryPostgresInputs
{
    public string Host { get; init; } = "postgres";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "sentry";
    public string Username { get; init; } = "sentry";
    public required ISecret Password { get; init; }
    public ISecret? ConnectionString { get; init; }
}

public sealed class SentryRedisInputs
{
    public string Host { get; init; } = "redis";
    public int Port { get; init; } = 6379;
    public int Database { get; init; } = 0;
    public ISecret? Password { get; init; }
    public ISecret? Url { get; init; }
}

public sealed class SentryDependencyInputs
{
    public required SentryPostgresInputs Postgres { get; init; }
    public required SentryRedisInputs Redis { get; init; }
    public string? KafkaBootstrapServers { get; init; }
    public string? ClickHouseHost { get; init; }
    public int? ClickHousePort { get; init; }
    public string? SnubaUrl { get; init; }
    public string? MemcachedServers { get; init; }
}

public sealed class SentryHostingArgs
{
    public required SentryDependencyInputs Dependencies { get; init; }
    public required ISecret SecretKey { get; init; }
    public IContainerRegistry? Registry { get; init; }
    public string ImageName { get; init; } = "ghcr.io/getsentry/sentry";
    public string ImageTag { get; init; } = "latest";
    public string? SystemUrlPrefix { get; init; }
    public IDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>();
    public IDictionary<string, ISecret> SecretEnvironmentVariables { get; init; } =
        new Dictionary<string, ISecret>();
    public ComputeServiceType? ServiceType { get; init; }
    public int ContainerPort { get; init; } = 9000;
    public int MinCount { get; init; } = 1;
    public int MaxCount { get; init; } = 1;
    public string MemoryLimit { get; init; } = "16Gi";
    public string CpuLimit { get; init; } = "4";
    public bool MirrorImageToRegistry { get; init; } = true;
}

public sealed class SentryRelayHostingArgs
{
    public IContainerRegistry? Registry { get; init; }
    public string ImageName { get; init; } = "ghcr.io/getsentry/relay";
    public string ImageTag { get; init; } = "latest";
    public string Mode { get; init; } = "managed";
    public required string UpstreamUrl { get; init; }
    public string Host { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 3000;
    public string? RelayId { get; init; }
    public string? PublicKey { get; init; }
    public ISecret? SecretKey { get; init; }
    public IDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>();
    public IDictionary<string, ISecret> SecretEnvironmentVariables { get; init; } =
        new Dictionary<string, ISecret>();
    public ComputeServiceType? ServiceType { get; init; }
    public int MinCount { get; init; } = 1;
    public int MaxCount { get; init; } = 1;
    public string MemoryLimit { get; init; } = "512Mi";
    public string CpuLimit { get; init; } = "1";
    public bool MirrorImageToRegistry { get; init; } = true;
}
