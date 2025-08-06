using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;

namespace PulumiCrossCloud.Core;

public static class ContainerRegistryExtensions
{
    public static Output<ImageDeploymentResult> DeployDockerImageExtension(this IContainerImage image, 
        string dockerfilePath, 
        string contextPath, 
        IEnumerable<string> additionalFilesToHash, 
        ImageArgs? imageArgs = null)
    {
        IContainerRegistry registry = image.Registry;
        var imageName = image.Name;
        var additionalFiles = additionalFilesToHash?.ToList() ?? new List<string>();
        
        // Create hash tracker to manage state and detect changes
        var hashTracker = new DockerImageHashTracker($"docker-hash-{registry.Name}", new DockerImageHashTrackerArgs
        {
            DockerfilePath = dockerfilePath,
            ContextPath = contextPath,
            AdditionalFiles = additionalFiles
        }, new ComponentResourceOptions 
        { 
            DependsOn = new[] { registry.Resource } 
        });

        var deploymentInfo = Output.Tuple(hashTracker.CombinedHash, hashTracker.ImageTag, hashTracker.ShouldDeploy, registry.RegistryUrlOutput);

        return deploymentInfo.Apply(tuple =>
        {
            var (hash, imageTag, shouldDeploy, registryUrl) = tuple;
            var hashTaggedImage = $"{registryUrl}/{imageName}:{imageTag}";
            var latestTaggedImage = $"{registryUrl}/{imageName}:latest";
            
            if (!shouldDeploy)
            {
                Log.Info("Hash unchanged - skipping Docker image deployment");
                return Output.Create(new ImageDeploymentResult
                {
                    ImageUrl = hashTaggedImage,
                    ImageTag = imageTag
                });
            }

            Log.Info("Hash changed - deploying Docker image...");
        
            var dockerImage = new Image($"{registry.Name}-image-{imageTag}", imageArgs ?? new ImageArgs
            {
                Tags = new[] { hashTaggedImage, latestTaggedImage },
                Context = new BuildContextArgs
                {
                    Location = contextPath,
                },
                Dockerfile = new DockerfileArgs
                {
                    Location = dockerfilePath,
                },
                BuildArgs = new InputMap<string>
                {
                    ["BUILDTIME"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["CONTENT_HASH"] = hash
                },
                // Use inline caching for better build performance
                CacheTo = new[]
                {
                    new CacheToArgs { Inline = null }
                },
                CacheFrom = new[]
                {
                    new CacheFromArgs
                    {
                        Registry = new CacheFromRegistryArgs
                        {
                            Ref = latestTaggedImage
                        }
                    }
                },
                Platforms = new[] { Platform.Linux_amd64 }, // Can add more platforms
                Push = true,
                // Registries = registry.GetDockerBuildRegistryConfig()
            }, new CustomResourceOptions
            {
                DependsOn = new Resource[] { registry.Resource, hashTracker },
            });
        
            return dockerImage.Ref.Apply(imageRef => new ImageDeploymentResult
            {
                ImageUrl = imageRef,
                ImageTag = imageTag
            });        
        }).Apply(output => output);
    }
}