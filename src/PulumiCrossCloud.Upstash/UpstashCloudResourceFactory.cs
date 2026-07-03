using Pulumi;
using PulumiCrossCloud.Core;
using Pulumi.Upstash;

namespace PulumiCrossCloud.Upstash;

public class UpstashCloudResourceFactory : CloudResourceFactory
{
    private readonly Pulumi.Upstash.Provider? _upstashProvider;

    /// <summary>
    /// Initializes a new instance of the Upstash resource factory.
    /// </summary>
    /// <param name="email">The email address registered with your Upstash account (Required).</param>
    /// <param name="apiKey">The API key generated from the Upstash Console (Required).</param>
    public UpstashCloudResourceFactory(Input<string> email, Input<string> apiKey)
        : base(CloudProvider.Upstash)
    {
        _upstashProvider = new Pulumi.Upstash.Provider("upstash-provider", new Pulumi.Upstash.ProviderArgs
        {
            Email = email,
            ApiKey = apiKey
        });
    }

    public UpstashCloudResourceFactory(Pulumi.Upstash.Provider? provider = null)
        : base(CloudProvider.Upstash)
    {
        _upstashProvider = provider;
    }

    public override IMemoryCacheDatabase CreateMemoryCache(string name, MemoryCacheOptions? options = null)
    {
        return new UpstashRedisDatabase(name, options as UpstashRedisOptions, _upstashProvider);
    }
}