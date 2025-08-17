using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsCloudResourceFactory : CloudResourceFactory
{
    public AwsCloudResourceFactory()
        : base(CloudProvider.AWS) { }

    public override ISecret CreateSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false
    )
    {
        return new AwsSecret(name, value);
    }

    public override IContainerRegistry CreateContainerRegistry(
        string name,
        bool createDefaultImage = true
    )
    {
        return new AwsContainerRegistry(name);
    }

    public override IContainerImage CreateContainerImage(string name, IContainerRegistry registry)
    {
        return new AwsContainerImage(name, registry);
    }

    public override ICompute CreateCompute(
        string name,
        IContainerRegistry registry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        ComputeServiceType? serviceType = null
    )
    {
        if (serviceType is null)
        {
            serviceType = ComputeServiceType.AppRunner;
        }
        return serviceType switch
        {
            ComputeServiceType.AppRunner
                => new AwsAppRunnerCompute(name, registry, imageName, imageTag, containerImage),
            _ => throw new ArgumentException($"Unsupported AWS service type: {serviceType}")
        };
    }

    public override IRole CreateRole(string name, string assumeRolePolicy)
    {
        return new AwsRole(name, assumeRolePolicy);
    }

    public override IBucket CreateBucket(
        string name,
        bool isPublic = false,
        BucketCdnConfiguration? cdn = null,
        bool versioningEnabled = false,
        IEnumerable<CorsRule>? corsRules = null
    )
    {
        return new AwsBucket(name, isPublic, cdn, versioningEnabled, corsRules);
    }
}
