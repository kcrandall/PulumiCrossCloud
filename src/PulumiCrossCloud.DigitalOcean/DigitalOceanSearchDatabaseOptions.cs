using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.DigitalOcean;

public sealed class DigitalOceanSearchDatabaseOptions : SearchDatabaseOptions
{
    public string? ClusterName { get; set; }
    public string Region { get; set; } = "nyc3";
    public string Version { get; set; } = "2";
    public global::Pulumi.DigitalOcean.DatabaseSlug Size { get; set; } =
        global::Pulumi.DigitalOcean.DatabaseSlug.DB_1VPCU2GB;
    public int NodeCount { get; set; } = 1;
    public string? ProjectId { get; set; }
    public string? PrivateNetworkUuid { get; set; }
    public int? StorageSizeMib { get; set; }
    public bool? StorageAutoscaleEnabled { get; set; }
    public int? StorageAutoscaleIncrementGib { get; set; }
    public int? StorageAutoscaleThresholdPercent { get; set; }
    public IList<string>? Tags { get; set; }
}
