using Pulumi;
using Pulumi.Gcp.Sql;
using Pulumi.Gcp.Sql.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpPostgresDatabase : IPostgresDatabase
{
    private readonly DatabaseInstance _instance;
    private readonly Database _database;
    private readonly User _user;
    private readonly Output<string> _password;

    public GcpPostgresDatabase(string name, IPostgresOptions? options = null, Pulumi.Gcp.Provider? provider = null)
    {
        Name = name;
        var gcpOptions = options as GcpPostgresOptions ?? new GcpPostgresOptions();
        var projectId = GcpProjectConfig.ProjectId;
        var region = options?.Region ?? GcpProjectConfig.Region;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };

        var databaseFlags = new List<DatabaseInstanceSettingsDatabaseFlagArgs>();
        if (options?.EnableLogicalReplication == true)
        {
            databaseFlags.Add(new DatabaseInstanceSettingsDatabaseFlagArgs
            {
                Name = "cloudsql.logical_decoding",
                Value = "on"
            });
        }

        _instance = new DatabaseInstance(name, new DatabaseInstanceArgs
        {
            Project = projectId,
            Region = region,
            DatabaseVersion = $"POSTGRES_{options?.MajorVersion ?? 17}",
            Settings = new DatabaseInstanceSettingsArgs
            {
                Tier = gcpOptions.Tier,
                IpConfiguration = new DatabaseInstanceSettingsIpConfigurationArgs
                {
                    Ipv4Enabled = options?.PubliclyAccessible ?? false,
                },
                DatabaseFlags = databaseFlags
            },
            DeletionProtection = false
        }, customResourceOptions);

        var dbName = options?.InitialDatabaseName ?? name;
        _database = new Database($"{name}-db", new DatabaseArgs
        {
            Instance = _instance.Name,
            Name = dbName,
            Project = projectId,
        }, customResourceOptions);

        var masterUser = options?.MasterUsername ?? "admin";
        var passwordResource = new Pulumi.Random.RandomPassword($"{name}-password", new Pulumi.Random.RandomPasswordArgs
        {
            Length = 16,
            Special = true,
            OverrideSpecial = "!#$%&*()-_=+[]{}<>:?"
        }, customResourceOptions);
        _password = passwordResource.Result;

        _user = new User($"{name}-user", new UserArgs
        {
            Instance = _instance.Name,
            Name = masterUser,
            Password = _password,
            Project = projectId,
        }, customResourceOptions);
    }

    public string Name { get; }
    public CloudProvider Provider => CloudProvider.GCP;
    public Output<string> Arn => _instance.Id;

    public Output<string> Host => _instance.PublicIpAddress; // Default to public IP for now
    public Output<int> Port => Output.Create(5432);
    public Output<string> Username => _user.Name;
    public Output<string> Password => _password;
    public Output<string> DatabaseName => _database.Name;

    public Output<string> ConnectionString => 
        Output.Format($"postgresql://{Username}:{Password}@{Host}:{Port}/{DatabaseName}");

    public void EnableExtension(string extensionName)
    {
        // Cloud SQL extensions are usually enabled via SQL or already present.
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params PostgresPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case PostgresPermissions.Connect:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["roles/cloudsql.client"],
                        Resources = [Arn]
                    });
                    break;
                case PostgresPermissions.Admin:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["roles/cloudsql.admin"],
                        Resources = [Arn]
                    });
                    break;
            }
        }
        return statements;
    }
}
