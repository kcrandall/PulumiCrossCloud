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
}
