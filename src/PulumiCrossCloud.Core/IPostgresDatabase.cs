using Pulumi;

namespace PulumiCrossCloud.Core;

public interface IPostgresDatabase : IRelationalDatabase, IPolicyProvider<PostgresPermissions>
{
    /// <summary>
    /// Enables a Postgres extension (e.g., "pgvector", "postgis").
    /// </summary>
    void EnableExtension(string extensionName);
}
