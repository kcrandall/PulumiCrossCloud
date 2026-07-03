using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Upstash;

public class UpstashRedisOptions : MemoryCacheOptions
{
    /// <summary>
    /// When enabled, data is encrypted in transit. TLS is enabled by default for newly created databases and cannot be disabled.
    /// </summary>
    public bool Tls { get; set; } = true;

    /// <summary>
    /// Enable eviction, to evict keys when your database reaches the max size.
    /// </summary>
    public bool Eviction { get; set; } = true;

    /// <summary>
    /// Upgrade to higher plans automatically when it hits quotas.
    /// </summary>
    public bool AutoScale { get; set; } = false;

    /// <summary>
    /// Budget for the database (default $20). It is used to limit the cost of the database. 
    /// If the budget is reached, the database will be throttled until the next month.
    /// </summary>
    public double? Budget { get; set; }

    /// <summary>
    /// Whether Prod Pack is enabled for the database.
    /// </summary>
    public bool ProdPack { get; set; } = false;

    /// <summary>
    /// Ip CIDR allowlist for the database. If not set, all IPs are allowed to connect to the database.
    /// </summary>
    public string[]? IpAllowlist { get; set; }

    /// <summary>
    /// Read regions for the database (Only works if region='global' and primary_region is set).
    /// Can be any combination of [us-east-1, us-west-1, us-west-2, eu-central-1, eu-west-1, sa-east-1, ap-southeast-1, ap-southeast-2], 
    /// excluding the one given as primary.
    /// </summary>
    public string[]? ReadRegions { get; set; }
}
