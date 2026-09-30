using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Neon;

public class NeonPostgresOptions : PostgresOptions
{
    /// <summary>
    /// Minimum compute units.
    /// </summary>
    public double? MinCu { get; set; }

    /// <summary>
    /// Maximum compute units.
    /// </summary>
    public double? MaxCu { get; set; }

    /// <summary>
    /// Inactivity before suspending the default compute: 300 seconds for the Free plan,
    /// 0 for Neon's default, or -1 to stay active (requires a paid Neon plan).
    /// Null leaves the existing setting unchanged.
    /// </summary>
    public int? SuspendTimeoutSeconds { get; set; }
}
