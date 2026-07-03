using Pulumi;
using Pulumi.AzureNative.DBforPostgreSQL;
using Pulumi.AzureNative.DBforPostgreSQL.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzurePostgresDatabase : IPostgresDatabase
{
    private readonly Server _server;
    private readonly Database _database;
    private readonly Output<string> _password;

    public AzurePostgresDatabase(string name, string resourceGroupName, IPostgresOptions? options = null, Pulumi.AzureNative.Provider? provider = null)
    {
        Name = name;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };
        var masterUser = options?.MasterUsername ?? "adminuser";
        
        var passwordResource = new Pulumi.Random.RandomPassword($"{name}-password", new Pulumi.Random.RandomPasswordArgs
        {
            Length = 16,
            Special = true,
            OverrideSpecial = "!#$%&*()-_=+[]{}<>:?"
        }, customResourceOptions);
        _password = passwordResource.Result;

        var serverArgs = new ServerArgs
        {
            ResourceGroupName = resourceGroupName,
            ServerName = name,
            Location = options?.Region ?? AzureProjectConfig.Region, 
            AdministratorLogin = masterUser,
            AdministratorLoginPassword = _password,
            Version = options?.MajorVersion.ToString() ?? "16",
            Sku = new SkuArgs
            {
                Name = "Standard_B1ms",
                Tier = SkuTier.Burstable,
            },
            Storage = new StorageArgs
            {
                StorageSizeGB = 32,
            },
            Backup = new Pulumi.AzureNative.DBforPostgreSQL.Inputs.BackupArgs
            {
                BackupRetentionDays = 7,
                GeoRedundantBackup = "Disabled",
            },
            Network = new NetworkArgs
            {
                // If publicly accessible, we might need firewall rules
            }
        };

        _server = new Server(name, serverArgs, customResourceOptions);

        var dbName = options?.InitialDatabaseName ?? name.Replace("-", "_");
        _database = new Database(dbName, new DatabaseArgs
        {
            ResourceGroupName = resourceGroupName,
            ServerName = _server.Name,
            DatabaseName = dbName,
        }, customResourceOptions);

        if (options?.EnableLogicalReplication == true)
        {
            // Enabling logical replication on Azure usually requires a configuration update
            new Configuration("wal-level-logical", new ConfigurationArgs
            {
                ResourceGroupName = resourceGroupName,
                ServerName = _server.Name,
                ConfigurationName = "wal_level",
                Value = "logical",
                Source = "user-override"
            }, customResourceOptions);
        }
    }

    public string Name { get; }
    public CloudProvider Provider => CloudProvider.Azure;
    public Output<string> Arn => _server.Id;

    public Output<string> Host => _server.FullyQualifiedDomainName.Apply(x => x ?? string.Empty);
    public Output<int> Port => Output.Create(5432);
    public Output<string> Username => _server.AdministratorLogin.Apply(x => x ?? string.Empty);
    public Output<string> Password => _password;
    public Output<string> DatabaseName => _database.Name;

    public Output<string> ConnectionString => 
        Output.Format($"postgresql://{Username}:{Password}@{Host}:{Port}/{DatabaseName}");

    public void EnableExtension(string extensionName)
    {
        // Azure extensions are enabled via configurations or SQL
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params PostgresPermissions[] permissions)
    {
        // Maps to Azure roles like "PostgreSQL Adapter User"
        return [];
    }
}
