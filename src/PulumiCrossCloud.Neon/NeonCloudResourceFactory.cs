using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Neon;

public class NeonCloudResourceFactory : CloudResourceFactory
{
    private readonly Pulumi.Neon.Provider? _providerResource;

    public NeonCloudResourceFactory()
        : this((Pulumi.Neon.Provider?)null)
    {
    }

    public NeonCloudResourceFactory(string? apiKey)
        : base(CloudProvider.Neon)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            _providerResource = new Pulumi.Neon.Provider("neon-provider", new Pulumi.Neon.ProviderArgs
            {
                ApiKey = apiKey
            });
        }
    }

    public NeonCloudResourceFactory(Input<string> apiKey)
        : base(CloudProvider.Neon)
    {
        _providerResource = new Pulumi.Neon.Provider("neon-provider", new Pulumi.Neon.ProviderArgs
        {
            ApiKey = apiKey
        });
    }

    public NeonCloudResourceFactory(Pulumi.Neon.Provider? provider)
        : base(CloudProvider.Neon)
    {
        _providerResource = provider;
    }

    public override IPostgresDatabase CreatePostgresDatabase(
        string name,
        IPostgresOptions? options = null
    )
    {
        return new NeonPostgresDatabase(name, options, _providerResource);
    }
}
