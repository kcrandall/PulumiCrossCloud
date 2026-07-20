using PulumiCrossCloud.Core;
using Xunit;

namespace PulumiCrossCloud.Core.Tests;

public class CloudResourceFactoryTests
{
    [Fact]
    public void CloudProvider_AWS_ShouldBeValid()
    {
        var provider = CloudProvider.AWS;
        Assert.Equal(CloudProvider.AWS, provider);
    }

    [Fact]
    public void ComputeServiceType_Fargate_ShouldBeValid()
    {
        var serviceType = ComputeServiceType.Fargate;
        Assert.Equal(ComputeServiceType.Fargate, serviceType);
        Assert.DoesNotContain("AppRunner", Enum.GetNames<ComputeServiceType>());
    }

    [Theory]
    [InlineData(SecretPermissions.Read)]
    [InlineData(SecretPermissions.Write)]
    [InlineData(SecretPermissions.Delete)]
    public void SecretPermissions_ShouldHaveValidValues(SecretPermissions permission)
    {
        Assert.True(Enum.IsDefined(typeof(SecretPermissions), permission));
    }

    [Theory]
    [InlineData(ContainerRegistryPermissions.Pull)]
    [InlineData(ContainerRegistryPermissions.Push)]
    [InlineData(ContainerRegistryPermissions.GetAuthToken)]
    public void ContainerRegistryPermissions_ShouldHaveValidValues(
        ContainerRegistryPermissions permission
    )
    {
        Assert.True(Enum.IsDefined(typeof(ContainerRegistryPermissions), permission));
    }
}
