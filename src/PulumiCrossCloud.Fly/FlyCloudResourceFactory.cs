using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Fly;

public sealed class FlyCloudResourceFactory : CloudResourceFactory
{
    private readonly string _orgSlug;
    private readonly string _region;
    private readonly global::Pulumi.Fly.Provider? _flyProvider;

    public FlyCloudResourceFactory(
        string orgSlug = "personal",
        string region = "iad",
        string? apiToken = null,
        string? flyctlPath = null
    )
        : base(CloudProvider.Fly)
    {
        _orgSlug = orgSlug;
        _region = region;
        var providerArgs = new global::Pulumi.Fly.ProviderArgs
        {
            OrgSlug = orgSlug
        };
        if (apiToken is not null)
        {
            providerArgs.ApiToken = apiToken;
        }
        if (flyctlPath is not null)
        {
            providerArgs.FlyctlPath = flyctlPath;
        }

        _flyProvider = new global::Pulumi.Fly.Provider(
            "fly-provider",
            providerArgs
        );
    }

    public override ISecret CreateSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false
    ) => new FlySecret(name, name, value, ignoreChangesAfterCreate, _flyProvider);

    public ISecret CreateAppSecret(
        string name,
        string appName,
        string value,
        bool ignoreChangesAfterCreate = false
    ) => new FlySecret(name, appName, value, ignoreChangesAfterCreate, _flyProvider);

    public override IContainerRegistry CreateContainerRegistry(
        string name,
        bool createDefaultImage = true
    ) => new FlyContainerRegistry(name, name);

    public IContainerRegistry CreateContainerRegistry(string name, string appName) =>
        new FlyContainerRegistry(name, appName);

    public override ICompute CreateCompute(
        string name,
        IContainerRegistry registry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        ComputeServiceType? serviceType = null
    )
    {
        if (serviceType is not null && serviceType != ComputeServiceType.FlyMachine)
        {
            throw new NotSupportedException($"Fly.io supports {ComputeServiceType.FlyMachine}, not {serviceType}.");
        }

        return new FlyMachineCompute(
            name,
            registry,
            imageName,
            imageTag,
            containerImage,
            _orgSlug,
            _region,
            _flyProvider
        );
    }
}
