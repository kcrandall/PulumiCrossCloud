using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aiven;

public class AivenSearchDatabaseOptions : SearchDatabaseOptions, IAivenDatabaseOptions
{
    public const string FreeOpenSearchPlan = "free-4-20";
    public const string DefaultFreeOpenSearchCloud = "do-nyc";

    public string? Plan { get; set; }
    public string? Cloud { get; set; }
    public string? Project { get; set; }
    public bool UseFreeTier { get; set; }
}
