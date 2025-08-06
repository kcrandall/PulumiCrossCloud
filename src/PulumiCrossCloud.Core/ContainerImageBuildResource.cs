using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;

namespace PulumiCrossCloud.Core;

public class ContainerImageBuildResource : ComponentResource
{
    public Output<string> ImageUrl { get; }
    public Output<string> ImageTag { get; }
    public Output<Image?> ImageResource { get; }
    public DockerImageHashTracker HashTracker { get; }

    public ContainerImageBuildResource(
        string name,
        IContainerRegistry registry,
        string imageName,
        string dockerfilePath,
        string contextPath,
        IEnumerable<string> additionalFilesToHash,
        ImageArgs? imageArgs = null) : base("crosscloud:core:ContainerImageBuildResource", name)
    {
        var additionalFiles = additionalFilesToHash?.ToList() ?? new List<string>();
        HashTracker = new DockerImageHashTracker($"docker-hash-{registry.Name}", new DockerImageHashTrackerArgs
        {
            DockerfilePath = dockerfilePath,
            ContextPath = contextPath,
            AdditionalFiles = additionalFiles
        }, new ComponentResourceOptions { DependsOn = [registry.Resource] });

        var deploymentInfo = Output.Tuple(HashTracker.CombinedHash, HashTracker.ImageTag, HashTracker.ShouldDeploy, registry.RegistryUrlOutput);

        var imageResult = deploymentInfo.Apply(tuple =>
        {
            var (hash, imageTag, shouldDeploy, registryUrl) = tuple;
            var hashTaggedImage = $"{registryUrl}/{imageName}:{imageTag}";
            var latestTaggedImage = $"{registryUrl}/{imageName}:latest";

            if (!shouldDeploy)
            {
                Log.Info("Hash unchanged - skipping Docker image deployment");
                return new ImageDeploymentResult
                {
                    ImageUrl = hashTaggedImage,
                    ImageTag = imageTag,
                    Image = null
                };
            }

            Log.Info("Hash changed - deploying Docker image...");
            var dockerImage = new Image($"{registry.Name}-image-{imageTag}", imageArgs ?? new ImageArgs
            {
                Tags = [hashTaggedImage, latestTaggedImage],
                Context = new BuildContextArgs { Location = contextPath },
                Dockerfile = new DockerfileArgs { Location = dockerfilePath },
                BuildArgs = new InputMap<string>
                {
                    ["BUILDTIME"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["CONTENT_HASH"] = hash
                },
                CacheTo = [new CacheToArgs { Inline = null }],
                CacheFrom = [new CacheFromArgs
                {
                    Registry = new CacheFromRegistryArgs { Ref = latestTaggedImage }
                }],
                Platforms = [Platform.Linux_amd64],
                Push = true,
            }, new CustomResourceOptions { DependsOn = [registry.Resource, HashTracker] });
            return new ImageDeploymentResult
            {
                ImageUrl = hashTaggedImage,
                ImageTag = imageTag,
                Image = dockerImage
            };
        });

        ImageUrl = imageResult.Apply(r => r.ImageUrl ?? "");
        ImageTag = imageResult.Apply(r => r.ImageTag ?? "");
        ImageResource = imageResult.Apply(r => r.Image);
    }
}

