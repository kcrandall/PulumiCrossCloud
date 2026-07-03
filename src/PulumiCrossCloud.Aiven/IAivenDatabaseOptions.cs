namespace PulumiCrossCloud.Aiven;

public interface IAivenDatabaseOptions
{
    string? Plan { get; set; }
    string? Cloud { get; set; }
    string? Project { get; set; }
}
