using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;
using Pulumi.Upstash;

namespace PulumiCrossCloud.Upstash;

public class UpstashRedisDatabase : IMemoryCacheDatabase
{
    private readonly RedisDatabase _database;

    public UpstashRedisDatabase(string name, UpstashRedisOptions? options = null, Pulumi.Upstash.Provider? provider = null)
    {
        Name = name;
        var upstashOptions = options ?? new UpstashRedisOptions();
        
        var args = new RedisDatabaseArgs
        {
            DatabaseName = name,
            Region = "global",
            PrimaryRegion = upstashOptions.Region ?? "us-east-1",
            Tls = upstashOptions.Tls,
            Eviction = upstashOptions.Eviction,
            AutoScale = upstashOptions.AutoScale,
            Budget = upstashOptions.Budget,
            ProdPack = upstashOptions.ProdPack
        };

        if (upstashOptions.IpAllowlist != null && upstashOptions.IpAllowlist.Length > 0)
        {
            foreach (var ip in upstashOptions.IpAllowlist)
            {
                args.IpAllowlists.Add(ip);
            }
        }

        if (upstashOptions.ReadRegions != null && upstashOptions.ReadRegions.Length > 0)
        {
            foreach (var region in upstashOptions.ReadRegions)
            {
                args.ReadRegions.Add(region);
            }
        }

        _database = new RedisDatabase(name, args, new CustomResourceOptions { Provider = provider });
    }

    public string Name { get; }
    public CloudProvider Provider => CloudProvider.Upstash;
    public Output<string> Arn => _database.DatabaseId;

    public Output<string> Host => _database.Endpoint;
    public Output<int> Port => _database.Port.Apply(p => (int)p);
    public Output<string> Username => Output.Create(string.Empty);
    public Output<string> Password => _database.Password;

    public Output<string> ConnectionString => 
        Output.Tuple(Host, Port, Password).Apply(t => 
            $"redis://:{t.Item3}@{t.Item1}:{t.Item2}");

    public IEnumerable<IPolicyStatement> GetPolicy(params MemoryCachePermissions[] permissions)
    {
        // Upstash doesn't use standard cloud IAM for data access
        return [];
    }
}
