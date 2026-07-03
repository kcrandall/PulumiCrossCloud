namespace PulumiCrossCloud.Core;

public interface IPostgresOptions
{
    int MajorVersion { get; set; }
    int? MinorVersion { get; set; }
    string? Region { get; set; }
    bool EnableLogicalReplication { get; set; }
    bool PubliclyAccessible { get; set; }
    string? MasterUsername { get; set; }
    string? InitialDatabaseName { get; set; }
}

public class PostgresOptions : IPostgresOptions
{
    public int MajorVersion { get; set; } = 17;
    public int? MinorVersion { get; set; }
    public string? Region { get; set; }
    public bool EnableLogicalReplication { get; set; } = false;
    public bool PubliclyAccessible { get; set; } = false;
    public string? MasterUsername { get; set; }
    public string? InitialDatabaseName { get; set; }
}
