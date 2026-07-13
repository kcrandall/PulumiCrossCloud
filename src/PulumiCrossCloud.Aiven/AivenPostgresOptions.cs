using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aiven;

public class AivenPostgresOptions : PostgresOptions, IAivenDatabaseOptions
{
    public string? Plan { get; set; }
    public string? Cloud { get; set; }
    public string? Project { get; set; }
}
