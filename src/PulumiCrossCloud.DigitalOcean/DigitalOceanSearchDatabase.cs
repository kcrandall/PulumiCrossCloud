using Pulumi;
using PulumiCrossCloud.Core;
using DO = global::Pulumi.DigitalOcean;

namespace PulumiCrossCloud.DigitalOcean;

public sealed class DigitalOceanSearchDatabase : ISearchDatabase
{
    private readonly DO.DatabaseCluster _cluster;

    public DigitalOceanSearchDatabase(
        string name,
        SearchDatabaseType type,
        ISearchDatabaseOptions? options = null,
        DO.Provider? provider = null
    )
    {
        if (type != SearchDatabaseType.OpenSearch)
        {
            throw new ArgumentException(
                $"DigitalOcean provider currently only supports OpenSearch for ISearchDatabase. Requested: {type}"
            );
        }

        Name = name;
        Type = type;

        var digitalOceanOptions = options as DigitalOceanSearchDatabaseOptions ?? new DigitalOceanSearchDatabaseOptions();
        ValidateOptions(digitalOceanOptions);

        var clusterArgs = new DO.DatabaseClusterArgs
        {
            Name = digitalOceanOptions.ClusterName ?? name,
            Engine = "opensearch",
            Version = digitalOceanOptions.Version,
            Size = digitalOceanOptions.Size,
            Region = digitalOceanOptions.Region,
            NodeCount = digitalOceanOptions.NodeCount,
        };

        if (!string.IsNullOrWhiteSpace(digitalOceanOptions.ProjectId))
        {
            clusterArgs.ProjectId = digitalOceanOptions.ProjectId;
        }

        if (!string.IsNullOrWhiteSpace(digitalOceanOptions.PrivateNetworkUuid))
        {
            clusterArgs.PrivateNetworkUuid = digitalOceanOptions.PrivateNetworkUuid;
        }

        if (digitalOceanOptions.StorageSizeMib is not null)
        {
            clusterArgs.StorageSizeMib = digitalOceanOptions.StorageSizeMib.Value.ToString();
        }

        if (digitalOceanOptions.Tags is not null)
        {
            foreach (var tag in digitalOceanOptions.Tags)
            {
                clusterArgs.Tags.Add(tag);
            }
        }

        if (digitalOceanOptions.StorageAutoscaleEnabled is not null)
        {
            clusterArgs.StorageAutoscale = new DO.Inputs.DatabaseClusterStorageAutoscaleArgs
            {
                Enabled = digitalOceanOptions.StorageAutoscaleEnabled.Value,
                IncrementGib = digitalOceanOptions.StorageAutoscaleIncrementGib,
                ThresholdPercent = digitalOceanOptions.StorageAutoscaleThresholdPercent,
            };
        }

        _cluster = new DO.DatabaseCluster(
            name,
            clusterArgs,
            new CustomResourceOptions { Provider = provider }
        );
    }

    public string Name { get; }
    public CloudProvider Provider => CloudProvider.DigitalOcean;
    public Output<string> Arn => _cluster.ClusterUrn;
    public SearchDatabaseType Type { get; }
    public Output<string> Host => _cluster.Host;
    public Output<int> Port => _cluster.Port;
    public Output<string> Username => _cluster.User;
    public Output<string> Password => _cluster.Password;
    public Output<string> ConnectionString => _cluster.Uri;
    public DO.DatabaseCluster Cluster => _cluster;

    public IEnumerable<IPolicyStatement> GetPolicy(params SearchDatabasePermissions[] permissions) => [];

    private static void ValidateOptions(DigitalOceanSearchDatabaseOptions options)
    {
        if (options.NodeCount < 1)
        {
            throw new ArgumentException("DigitalOcean OpenSearch requires at least one database node.");
        }

        if (string.IsNullOrWhiteSpace(options.Region))
        {
            throw new ArgumentException("DigitalOcean OpenSearch requires a region.");
        }

        if (string.IsNullOrWhiteSpace(options.Version))
        {
            throw new ArgumentException("DigitalOcean OpenSearch requires an engine version.");
        }
    }
}
