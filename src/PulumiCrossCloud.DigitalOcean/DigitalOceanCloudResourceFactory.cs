using Pulumi;
using PulumiCrossCloud.Core;
using DO = global::Pulumi.DigitalOcean;

namespace PulumiCrossCloud.DigitalOcean;

public sealed class DigitalOceanCloudResourceFactory : CloudResourceFactory
{
    private readonly string _defaultAppRegion;
    private readonly string _defaultRegistryRegion;
    private readonly string _defaultDatabaseRegion;
    private readonly string _defaultInstanceSizeSlug;
    private readonly string _defaultRegistrySubscriptionTierSlug;
    private readonly DO.Provider _digitalOceanProvider;

    public DigitalOceanCloudResourceFactory(
        string defaultAppRegion = "nyc",
        string defaultRegistryRegion = "nyc3",
        string defaultInstanceSizeSlug = "apps-s-1vcpu-1gb",
        string defaultRegistrySubscriptionTierSlug = "starter",
        string? token = null,
        string defaultDatabaseRegion = "nyc3"
    )
        : base(CloudProvider.DigitalOcean)
    {
        _defaultAppRegion = defaultAppRegion;
        _defaultRegistryRegion = defaultRegistryRegion;
        _defaultDatabaseRegion = defaultDatabaseRegion;
        _defaultInstanceSizeSlug = defaultInstanceSizeSlug;
        _defaultRegistrySubscriptionTierSlug = defaultRegistrySubscriptionTierSlug;

        var providerArgs = new DO.ProviderArgs();
        if (token is not null)
        {
            providerArgs.Token = token;
        }

        _digitalOceanProvider = new DO.Provider("digitalocean-provider", providerArgs);
    }

    public override ISecret CreateSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false
    ) => new DigitalOceanSecret(name, value, ignoreChangesAfterCreate);

    public override ISecret CreateSecret(
        string name,
        Output<string> value,
        bool ignoreChangesAfterCreate = false
    ) => new DigitalOceanSecret(name, value, ignoreChangesAfterCreate);

    public override IContainerRegistry CreateContainerRegistry(
        string name,
        bool createDefaultImage = true
    ) =>
        new DigitalOceanContainerRegistry(
            name,
            _defaultRegistryRegion,
            _defaultRegistrySubscriptionTierSlug,
            _digitalOceanProvider
        );

    public override IContainerImage CreateContainerImage(string name, IContainerRegistry registry) =>
        new DigitalOceanContainerImage(name, registry);

    public override ICompute CreateCompute(
        string name,
        IContainerRegistry registry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        ComputeServiceType? serviceType = null
    )
    {
        if (serviceType is not null && serviceType != ComputeServiceType.DigitalOceanAppPlatform)
        {
            throw new NotSupportedException(
                $"DigitalOcean supports {ComputeServiceType.DigitalOceanAppPlatform}, not {serviceType}."
            );
        }

        return new DigitalOceanAppPlatformCompute(
            name,
            registry,
            imageName,
            imageTag,
            containerImage,
            _defaultAppRegion,
            _defaultInstanceSizeSlug,
            _digitalOceanProvider
        );
    }

    public override ISearchDatabase CreateSearchDatabase(
        string name,
        SearchDatabaseType type,
        ISearchDatabaseOptions? options = null
    )
    {
        var digitalOceanOptions = options as DigitalOceanSearchDatabaseOptions;
        digitalOceanOptions ??= new DigitalOceanSearchDatabaseOptions
        {
            Region = _defaultDatabaseRegion
        };

        return new DigitalOceanSearchDatabase(name, type, digitalOceanOptions, _digitalOceanProvider);
    }
}
