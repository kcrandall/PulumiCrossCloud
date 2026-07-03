using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpPostgresOptions : PostgresOptions
{
    /// <summary>
    /// The machine type to use (e.g., "db-f1-micro", "db-g1-small").
    /// </summary>
    public string Tier { get; set; } = "db-f1-micro";
}
