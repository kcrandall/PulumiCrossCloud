using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Koyeb;

public sealed class KoyebCloudResourceFactory : CloudResourceFactory
{
    private readonly global::Pulumi.Koyeb.Provider? _koyebProvider;
    private readonly string _defaultRegion;
    private readonly string _defaultInstanceType;

    public KoyebCloudResourceFactory(string defaultRegion = "was", string defaultInstanceType = "micro")
        : base(CloudProvider.Koyeb)
    {
        _defaultRegion = defaultRegion;
        _defaultInstanceType = defaultInstanceType;
        _koyebProvider = new global::Pulumi.Koyeb.Provider("koyeb-provider");
    }

    public override ISecret CreateSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false
    ) => new KoyebSecret(name, value, ignoreChangesAfterCreate, _koyebProvider);

    public override IContainerRegistry CreateContainerRegistry(
        string name,
        bool createDefaultImage = true
    ) => new KoyebContainerRegistry(name);

    public override ICompute CreateCompute(
        string name,
        IContainerRegistry registry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        ComputeServiceType? serviceType = null
    )
    {
        if (serviceType is not null && serviceType != ComputeServiceType.KoyebService)
        {
            throw new NotSupportedException($"Koyeb supports {ComputeServiceType.KoyebService}, not {serviceType}.");
        }

        return new KoyebServiceCompute(
            name,
            registry,
            imageName,
            imageTag,
            containerImage,
            _defaultRegion,
            _defaultInstanceType,
            _koyebProvider
        );
    }
}
