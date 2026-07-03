using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aiven;

public class AivenSearchDatabaseOptions : SearchDatabaseOptions, IAivenDatabaseOptions
{
    public string? Plan { get; set; }
    public string? Cloud { get; set; }
    public string? Project { get; set; }
}
