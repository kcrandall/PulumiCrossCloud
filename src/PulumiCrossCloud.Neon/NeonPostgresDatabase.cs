using Pulumi;
using PulumiCrossCloud.Core;
using Pulumi.Neon;

namespace PulumiCrossCloud.Neon;

/// <summary>
/// Neon implementation of the Postgres database abstraction.
/// </summary>
/// <remarks>
/// This implementation follows a "One Project Per Resource" strategy. 
/// In the Neon hierarchy:
/// - Project: The top-level container for billing and isolation.
/// - Branch: A data-isolated branch (defaults to 'main').
/// - Endpoint: The serverless compute resource (CPU/RAM) providing the Host/Port.
/// - Database: The logical PostgreSQL database.
/// 
/// By creating a new Project for every IPostgresDatabase instance, we ensure strict 
/// isolation between environments (e.g., Dev vs Staging) and simplify the lifecycle 
/// management within Pulumi.
/// </remarks>
public class NeonPostgresDatabase : IPostgresDatabase
{
    private readonly Project _project;
    private readonly Role _role;
    private readonly Database _database;

    /// <summary>
    /// Initializes a new instance of the Neon Postgres database by orchestrating
    /// all required Neon resources (Project, Role, Database, and Endpoint).
    /// </summary>
    public NeonPostgresDatabase(string name, IPostgresOptions? options = null, Pulumi.Neon.Provider? provider = null)
    {
        Name = name;
        var neonOptions = options as NeonPostgresOptions ?? new NeonPostgresOptions();
        var customResourceOptions = new CustomResourceOptions { Provider = provider };
        
        _project = new Project($"{name}-project", new ProjectArgs
        {
            Name = name,
            RegionId = options?.Region ?? "aws-us-east-2",
            PgVersion = options?.MajorVersion ?? 17,
            HistoryRetentionSeconds = 21600, // 6 hours - Maximum for Neon Free Tier
            EnableLogicalReplication = options?.EnableLogicalReplication == true ? "yes" : "no",
        }, new CustomResourceOptions
        {
            Provider = provider,
            // Ignore all computed outputs from Neon API to prevent drift
            IgnoreChanges = { 
                "enableLogicalReplication", "branch", "defaultEndpointSettings", "pgVersion",
                "computeProvisioner", "databaseHost", "databaseName", "databaseUser",
                "defaultBranchId", "defaultEndpointId", "orgId", "quota"
            }
        });
        var branchId = _project.DefaultBranchId;

        var masterUser = options?.MasterUsername ?? "admin";
        _role = new Role($"{name}-role", new RoleArgs
        {
            ProjectId = _project.Id,
            BranchId = branchId,
            Name = masterUser,
        }, new CustomResourceOptions { Provider = provider, DependsOn = [_project] });

        var dbName = options?.InitialDatabaseName ?? name;
        _database = new Database($"{name}-db", new DatabaseArgs
        {
            ProjectId = _project.Id,
            BranchId = branchId,
            Name = dbName,
            OwnerName = _role.Name.Apply(n => n!),
        }, new CustomResourceOptions { Provider = provider, DependsOn = [_role] });

        // On Neon Free Tier, a 'read_write' endpoint is created automatically.
        // We use the Project's default outputs to get connection details.
        _host = _project.DatabaseHost;
    }

    public string Name { get; }
    public CloudProvider Provider => CloudProvider.Neon;
    public Output<string> Arn => _project.Id;

    private readonly Output<string> _host;
    public Output<string> Host => _host;
    public Output<int> Port => Output.Create(5432);
    public Output<string> Username => _role.Name.Apply(n => n!);
    public Output<string> Password => _role.Password.Apply(p => p!);
    public Output<string> DatabaseName => _database.Name.Apply(n => n!);

    public Output<string> ConnectionString => 
        Output.Format($"postgresql://{Username}:{Password}@{Host}:{Port}/{DatabaseName}");

    public Output<string> ProjectId => _project.Id;
    public Output<string> BranchId => _project.DefaultBranchId;

    public void EnableExtension(string extensionName)
    {
        // Neon extensions are often enabled via SQL or pre-installed.
        // The provider might not have a direct resource for this yet.
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params PostgresPermissions[] permissions)
    {
        // Neon doesn't use AWS/GCP IAM for DB access in the same way.
        // Usually handled via connection strings.
        return [];
    }
}
