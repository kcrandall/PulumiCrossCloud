using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsSearchDatabaseOptions : SearchDatabaseOptions
{
    public string? CollectionName { get; set; }
    public string? CollectionGroupName { get; set; }
    public string? Description { get; set; }
    public string Generation { get; set; } = "NEXTGEN";
    public string CollectionType { get; set; } = "SEARCH";
    public string StandbyReplicas { get; set; } = "ENABLED";
    public double MinIndexingCapacityInOcu { get; set; } = 0;
    public double MaxIndexingCapacityInOcu { get; set; } = 96;
    public double MinSearchCapacityInOcu { get; set; } = 0;
    public double MaxSearchCapacityInOcu { get; set; } = 96;
    public Input<string>? KmsKeyArn { get; set; }
    public bool AwsOwnedKey { get; set; } = true;
    public string? ServerlessVectorAcceleration { get; set; }
    public string? Region { get; set; }
    public IDictionary<string, string>? Tags { get; set; }
}
