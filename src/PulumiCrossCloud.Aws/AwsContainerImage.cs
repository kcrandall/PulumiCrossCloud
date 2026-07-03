using Pulumi;
using Pulumi.Aws;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsContainerImage : IContainerImage
{
    public Resource Resource { get; set; } = null!;
    public Resource ContainerImageBuildResource { get; set; } = null!;
    public string Name { get; }
    public Output<string> Arn => DeploymentResult.Apply(result => result.ImageUrl ?? string.Empty);
    public CloudProvider Provider => CloudProvider.AWS;
    public IContainerRegistry Registry { get; }
    public Output<ImageDeploymentResult> DeploymentResult { get; private set; } = null!;

    public AwsContainerImage(string name, IContainerRegistry registry)
    {
        Name = name;
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));

        if (registry.Provider != CloudProvider.AWS)
        {
            throw new ArgumentException("Registry must be an AWS registry", nameof(registry));
        }
    }

    Output<ImageDeploymentResult> PushImage(
        string dockerfilePath,
        string contextPath,
        ImageArgs? imageArgs = null
    )
    {
        var result = ((IContainerImage)this).PushImage(dockerfilePath, contextPath, imageArgs);
        DeploymentResult = result;
        return result;
    }

    /// <summary>
    /// AWS ECR-specific registry configuration - requires authentication credentials
    /// </summary>
    public InputList<RegistryArgs> GetDockerBuildRegistryConfig()
    {
        var awsRegistry = Registry as AwsContainerRegistry;
        var repository = awsRegistry?.Resource as Pulumi.Aws.Ecr.Repository;
        // Get ECR auth token
        var authToken = Pulumi.Aws.Ecr.GetAuthorizationToken.Invoke(
            new() { RegistryId = repository!.RegistryId, }
        );

        return new InputList<RegistryArgs>
        {
            new RegistryArgs
            {
                Address = awsRegistry!.RegistryUrlOutput.Apply(url => url.Split('/')[0]), // Just the registry domain
                Username = authToken.Apply(token => token.UserName),
                Password = authToken.Apply(token => token.Password)
            }
        };
    }

    private static string ExtractRegistryServer(string registryUrl)
    {
        try
        {
            // Handle URLs that may or may not have protocol
            var urlToParse = registryUrl.StartsWith("http")
                ? registryUrl
                : $"https://{registryUrl}";
            var uri = new Uri(urlToParse);
            return uri.Host;
        }
        catch
        {
            // Fallback: assume the first part before '/' is the server
            return registryUrl.Split('/')[0];
        }
    }
}
