using Pulumi;
using Pulumi.Aws.Rds;
using Pulumi.Aws.Rds.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsPostgresDatabase : IPostgresDatabase
{
    private readonly Output<string> _host;
    private readonly Output<string> _username;
    private readonly Output<string> _password;
    private readonly Output<string> _databaseName;
    private readonly Resource _mainResource;

    public AwsPostgresDatabase(string name, IPostgresOptions? options = null, Pulumi.Aws.Provider? provider = null)
    {
        Name = name;
        var awsOptions = options as AwsPostgresOptions;
        var masterUser = options?.MasterUsername ?? "admin";
        var region = options?.Region ?? AwsProjectConfig.Region;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };
        
        var passwordResource = new Pulumi.Random.RandomPassword($"{name}-password", new Pulumi.Random.RandomPasswordArgs
        {
            Length = 16,
            Special = true,
            OverrideSpecial = "!#$%&*()-_=+[]{}<>:?"
        }, customResourceOptions);
        _password = passwordResource.Result;
        _username = Output.Create(masterUser);
        _databaseName = Output.Create(options?.InitialDatabaseName ?? name.Replace("-", "_"));

        // Create Subnet Group if subnets are provided
        SubnetGroup? subnetGroup = null;
        if (awsOptions?.SubnetIds is { Length: > 0 })
        {
            subnetGroup = new SubnetGroup($"{name}-sng", new SubnetGroupArgs
            {
                SubnetIds = awsOptions.SubnetIds,
                Description = $"Database subnet group for {name}"
            }, customResourceOptions);
        }

        if (awsOptions is AwsAuroraPostgresOptions auroraOptions)
        {
            var clusterArgs = new ClusterArgs
            {
                ClusterIdentifier = name,
                Engine = EngineType.AuroraPostgresql,
                EngineVersion = options?.MinorVersion.HasValue == true ? $"{options.MajorVersion}.{options.MinorVersion}" : $"{options?.MajorVersion ?? 16}.1",
                MasterUsername = masterUser,
                MasterPassword = _password,
                DatabaseName = _databaseName,
                SkipFinalSnapshot = true,
                AvailabilityZones = [region + "a", region + "b", region + "c"]
            };

            if (subnetGroup != null)
            {
                clusterArgs.DbSubnetGroupName = subnetGroup.Name;
            }

            if (auroraOptions.SecurityGroupIds != null)
            {
                clusterArgs.VpcSecurityGroupIds = auroraOptions.SecurityGroupIds;
            }

            if (auroraOptions.IsServerless)
            {
                clusterArgs.Serverlessv2ScalingConfiguration = new ClusterServerlessv2ScalingConfigurationArgs
                {
                    MinCapacity = auroraOptions.MinCapacity,
                    MaxCapacity = auroraOptions.MaxCapacity
                };
            }

            var cluster = new Cluster(name, clusterArgs, customResourceOptions);
            _mainResource = cluster;
            _host = cluster.Endpoint;

            // Create Primary Instance
            var primaryArgs = new ClusterInstanceArgs
            {
                Identifier = $"{name}-primary",
                ClusterIdentifier = cluster.Id,
                InstanceClass = auroraOptions.InstanceClass,
                Engine = EngineType.AuroraPostgresql,
                EngineVersion = cluster.EngineVersion.Apply(v => v!),
                PubliclyAccessible = options?.PubliclyAccessible ?? false,
            };
            if (subnetGroup != null) primaryArgs.DbSubnetGroupName = subnetGroup.Name;
            
            new ClusterInstance($"{name}-primary", primaryArgs, customResourceOptions);

            // Create Reader Instances
            for (int i = 0; i < auroraOptions.Readers.Count; i++)
            {
                var reader = auroraOptions.Readers[i];
                var readerArgs = new ClusterInstanceArgs
                {
                    Identifier = $"{name}-reader-{i}",
                    ClusterIdentifier = cluster.Id,
                    InstanceClass = reader.InstanceClass ?? auroraOptions.InstanceClass,
                    Engine = EngineType.AuroraPostgresql,
                    EngineVersion = cluster.EngineVersion.Apply(v => v!),
                    PubliclyAccessible = options?.PubliclyAccessible ?? false,
                    PromotionTier = reader.PromotionTier
                };
                if (subnetGroup != null) readerArgs.DbSubnetGroupName = subnetGroup.Name;

                new ClusterInstance($"{name}-reader-{i}", readerArgs, customResourceOptions);
            }
        }
        else
        {
            var rdsOptions = awsOptions as AwsRdsPostgresOptions ?? new AwsRdsPostgresOptions();
            
            var instanceArgs = new InstanceArgs
            {
                Identifier = name,
                Engine = "postgres",
                EngineVersion = options?.MinorVersion.HasValue == true ? $"{options.MajorVersion}.{options.MinorVersion}" : $"{options?.MajorVersion ?? 16}.1",
                InstanceClass = rdsOptions.InstanceClass,
                AllocatedStorage = rdsOptions.AllocatedStorage,
                StorageType = rdsOptions.StorageType,
                Username = masterUser,
                Password = _password,
                DbName = _databaseName,
                SkipFinalSnapshot = true,
                PubliclyAccessible = options?.PubliclyAccessible ?? false,
                AvailabilityZone = region + "a",
            };

            if (subnetGroup != null)
            {
                instanceArgs.DbSubnetGroupName = subnetGroup.Name;
            }

            if (rdsOptions.SecurityGroupIds != null)
            {
                instanceArgs.VpcSecurityGroupIds = rdsOptions.SecurityGroupIds;
            }
            
            var instance = new Instance(name, instanceArgs, customResourceOptions);
            
            _mainResource = instance;
            _host = instance.Address.Apply(a => a!);
        }
    }

    public string Name { get; }
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> Arn => _mainResource is Cluster c ? c.Arn : ((Instance)_mainResource).Arn;

    public Output<string> Host => _host;
    public Output<int> Port => Output.Create(5432);
    public Output<string> Username => _username;
    public Output<string> Password => _password;
    public Output<string> DatabaseName => _databaseName;

    public Output<string> ConnectionString => 
        Output.Format($"postgresql://{Username}:{Password}@{Host}:{Port}/{DatabaseName}");

    public void EnableExtension(string extensionName)
    {
        // RDS extensions are managed via Parameter Groups or SQL
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params PostgresPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case PostgresPermissions.Connect:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["rds-db:connect"],
                        Resources = [Arn]
                    });
                    break;
            }
        }
        return statements;
    }
}