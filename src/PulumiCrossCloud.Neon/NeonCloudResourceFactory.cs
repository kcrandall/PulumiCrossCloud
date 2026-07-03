using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Neon;

public class NeonCloudResourceFactory : CloudResourceFactory
{
    private readonly Pulumi.Neon.Provider? _providerResource;

    public NeonCloudResourceFactory(string? apiKey = null)
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

    public override IPostgresDatabase CreatePostgresDatabase(
        string name,
        IPostgresOptions? options = null
    )
    {
        return new NeonPostgresDatabase(name, options, _providerResource);
    }
}
