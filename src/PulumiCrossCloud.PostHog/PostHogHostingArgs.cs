using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.PostHog;

public sealed class PostHogDatabaseInputs
{
    public ISecret? Url { get; init; }
    public string Host { get; init; } = "postgres";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "posthog";
    public string Username { get; init; } = "posthog";
    public ISecret? Password { get; init; }
    public string? SslMode { get; init; }
}

public sealed class PostHogRedisInputs
{
    public ISecret? Url { get; init; }
    public string Host { get; init; } = "redis";
    public int Port { get; init; } = 6379;
    public int Database { get; init; }
    public ISecret? Password { get; init; }
}

public sealed class PostHogClickHouseInputs
{
    public string Host { get; init; } = "clickhouse";
    public int Port { get; init; } = 8123;
    public string Database { get; init; } = "posthog";
    public string? Username { get; init; }
    public ISecret? Password { get; init; }
    public bool Secure { get; init; }
    public bool Verify { get; init; }
    public bool DisableExternalSchemas { get; init; }
}

public sealed class PostHogObjectStorageInputs
{
    public string Bucket { get; init; } = "posthog";
    public string? Endpoint { get; init; }
    public string? PublicEndpoint { get; init; }
    public string? Region { get; init; }
    public string? AccessKeyId { get; init; }
    public ISecret? SecretAccessKey { get; init; }
}

public sealed class PostHogEmailInputs
{
    public bool Enabled { get; init; } = true;
    public string Host { get; init; } = "smtp";
    public int Port { get; init; } = 587;
    public string? User { get; init; }
    public ISecret? Password { get; init; }
    public string? DefaultFrom { get; init; }
    public bool UseTls { get; init; } = true;
    public bool UseSsl { get; init; }
}

public sealed class PostHogHostingArgs
{
    public required ISecret SecretKey { get; init; }
    public required string SiteUrl { get; init; }
    public ISecret? JwtSigningKey { get; init; }
    public ISecret? EncryptionSaltKeys { get; init; }
    public PostHogDatabaseInputs? Database { get; init; }
    public PostHogRedisInputs? Redis { get; init; }
    public PostHogClickHouseInputs? ClickHouse { get; init; }
    public string? KafkaHosts { get; init; }
    public string? KafkaUrl { get; init; }
    public PostHogObjectStorageInputs? ObjectStorage { get; init; }
    public PostHogEmailInputs? Email { get; init; }
    public IContainerRegistry? Registry { get; init; }
    public string ImageName { get; init; } = "posthog/posthog";
    public string ImageTag { get; init; } = "latest";
    public bool IsBehindProxy { get; init; } = true;
    public bool SecureCookies { get; init; } = true;
    public bool TrustAllProxies { get; init; }
    public bool DisableSecureSslRedirect { get; init; }
    public bool DisablePaidFeatureShowcasing { get; init; } = true;
    public IDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>();
    public IDictionary<string, ISecret> SecretEnvironmentVariables { get; init; } =
        new Dictionary<string, ISecret>();
    public ComputeServiceType? ServiceType { get; init; }
    public int ContainerPort { get; init; } = 8000;
    public int MinCount { get; init; } = 1;
    public int MaxCount { get; init; } = 1;
    public string MemoryLimit { get; init; } = "4Gi";
    public string CpuLimit { get; init; } = "2";
    public bool MirrorImageToRegistry { get; init; } = true;
}
