namespace PulumiCrossCloud.Core;

/// <summary>
/// Cross-cloud health check configuration abstraction for compute resources.
/// </summary>
public interface IHealthCheckConfiguration
{
    /// <summary>
    /// The path to check for health (e.g. "/healthz").
    /// </summary>
    string Path { get; set; }

    /// <summary>
    /// The interval in seconds between health checks.
    /// </summary>
    int IntervalSeconds { get; set; }

    /// <summary>
    /// The timeout in seconds for each health check.
    /// </summary>
    int TimeoutSeconds { get; set; }

    /// <summary>
    /// The number of consecutive failures before marking unhealthy.
    /// </summary>
    int UnhealthyThreshold { get; set; }

    /// <summary>
    /// The number of consecutive successes before marking healthy.
    /// </summary>
    int HealthyThreshold { get; set; }

    /// <summary>
    /// Startup probe
    /// Not all clouds support startup probes.
    /// </summary>
    int? StartupTimeoutSeconds { get; set; }

    /// <summary>
    /// Startup probe
    /// Not all clouds support startup probes.
    /// </summary>
    int? StartupIntervalSeconds { get; set; }

    /// <summary>
    /// Startup probe
    /// Not all clouds support startup probes.
    /// </summary>
    int? StartupUnhealthyThreshold { get; set; }
}

/// <summary>
/// Default implementation of IHealthCheckConfiguration.
/// </summary>
public class HealthCheckConfiguration : IHealthCheckConfiguration
{
    public string Path { get; set; } = "/healthz";
    public int IntervalSeconds { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 5;
    public int UnhealthyThreshold { get; set; } = 3;
    public int HealthyThreshold { get; set; } = 2;
    public int? StartupTimeoutSeconds { get; set; }
    public int? StartupIntervalSeconds { get; set; }
    public int? StartupUnhealthyThreshold { get; set; }
}
