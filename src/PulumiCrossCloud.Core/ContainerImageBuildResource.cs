using Pulumi;
using Pulumi.Command.Local;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;

namespace PulumiCrossCloud.Core;

public class ContainerImageBuildResource : ComponentResource
{
    public Output<string> ImageUrl { get; }
    public Output<string> ImageTag { get; }
    public Output<Image?> ImageResource { get; }

    public ContainerImageBuildResource(
        string name,
        IContainerRegistry registry,
        string imageName,
        string dockerfilePath,
        string contextPath,
        ImageArgs? imageArgs = null
    )
        : base("crosscloud:core:ContainerImageBuildResource", name)
    {
        var registryUrl = registry.RegistryUrlOutput; // Output<string>
        var latestImageRef = registryUrl.Apply(url => $"{url}/{imageName}:latest");

        // Always do the digest check, but conditionally create resources based on results
        var digestComparison = CheckDigests(
            registry,
            imageName,
            latestImageRef,
            dockerfilePath,
            contextPath,
            name
        );

        // Conditionally create the Image resource only if needed
        var imageResult = digestComparison.Apply(comparison =>
        {
            if (comparison.ChangesNeeded)
            {
                if (Deployment.Instance.IsDryRun)
                {
                    // In preview mode, just report what would happen
                    Log.Info(
                        $"[PREVIEW] Would build and push {comparison.ImageRef} (digest mismatch)"
                    );
                    return new ImageDeploymentResult
                    {
                        ImageUrl = comparison.ImageRef,
                        ImageTag = "latest",
                        Image = null // No actual resource created in preview
                    };
                }
                else
                {
                    // In apply mode, actually create the image
                    Log.Info($"Building and pushing {comparison.ImageRef} (digest mismatch)");
                    var dockerImage = new Image(
                        $"{registry.Name}-image-latest",
                        imageArgs
                            ?? new ImageArgs
                            {
                                Tags = [comparison.ImageRef],
                                Context = new BuildContextArgs { Location = contextPath },
                                Dockerfile = new DockerfileArgs { Location = dockerfilePath },
                                CacheTo = [new CacheToArgs { Inline = null }],
                                CacheFrom =
                                [
                                    new CacheFromArgs
                                    {
                                        Registry = new CacheFromRegistryArgs
                                        {
                                            Ref = comparison.ImageRef
                                        }
                                    }
                                ],
                                Platforms = [Platform.Linux_amd64],
                                Push = true,
                                Registries = registry.GetDockerBuildRegistryConfig()
                            },
                        new CustomResourceOptions { DependsOn = [registry.Resource] }
                    );
                    return new ImageDeploymentResult
                    {
                        ImageUrl = comparison.ImageRef,
                        ImageTag = "latest",
                        Image = dockerImage
                    };
                }
            }
            else
            {
                // No changes needed
                Log.Info($"Image digests match - no changes needed for {comparison.ImageRef}");
                return new ImageDeploymentResult
                {
                    ImageUrl = comparison.ImageRef,
                    ImageTag = "latest",
                    Image = null
                };
            }
        });

        ImageUrl = imageResult.Apply(r => r.ImageUrl ?? "");
        ImageTag = imageResult.Apply(r => r.ImageTag ?? "");
        ImageResource = imageResult.Apply(r => r.Image);
    }

    private Output<DigestComparison> CheckDigests(
        IContainerRegistry registry,
        string imageName,
        Output<string> latestImageRef,
        string dockerfilePath,
        string contextPath,
        string name
    )
    {
        // 1) Remote digest check
        var manifestInspect = new Command(
            $"inspect-manifest-{registry.Name}-{imageName}",
            new CommandArgs
            {
                Create = latestImageRef.Apply(img =>
                    $"docker manifest inspect {img} 2>/dev/null | jq -r '.config.digest // empty' || echo 'no-manifest'"
                ),
                Update = latestImageRef.Apply(img =>
                    $"docker manifest inspect {img} 2>/dev/null | jq -r '.config.digest // empty' || echo 'no-manifest'"
                ),
                Delete = "echo 'no-manifest'",
                Triggers = new[] { imageName }
            }
        );
        var remoteDigest = manifestInspect.Stdout.Apply(o => (o ?? string.Empty).Trim());

        // 2) Local digest calculation
        var localDigestCmd = new Command(
            $"compute-local-digest-{registry.Name}-{imageName}",
            new CommandArgs
            {
                Create =
                    $"TMP_TAG=cc-temp-{name}-$(date +%s); "
                    + $"docker buildx build --provenance=false --load -f '{dockerfilePath}' '{contextPath}' -t $TMP_TAG >/dev/null && "
                    + "docker inspect $TMP_TAG | jq -r '.[0].Id' && docker rmi $TMP_TAG >/dev/null 2>&1 || true",
                Update =
                    $"TMP_TAG=cc-temp-{name}-$(date +%s); "
                    + $"docker buildx build --provenance=false --load -f '{dockerfilePath}' '{contextPath}' -t $TMP_TAG >/dev/null && "
                    + "docker inspect $TMP_TAG | jq -r '.[0].Id' && docker rmi $TMP_TAG >/dev/null 2>&1 || true",
                Delete = "echo ''",
                Triggers = new[] { dockerfilePath, contextPath }
            }
        );
        var localDigest = localDigestCmd.Stdout.Apply(o => (o ?? string.Empty).Trim());

        // 3) Compare digests and determine if changes are needed
        return Output
            .Tuple(remoteDigest, localDigest, latestImageRef)
            .Apply(tuple =>
            {
                var (remote, local, imageRef) = tuple;

                bool remoteValid =
                    !string.IsNullOrWhiteSpace(remote)
                    && remote != "no-manifest"
                    && remote != "null";
                bool localValid = !string.IsNullOrWhiteSpace(local);

                bool changesNeeded = !(
                    remoteValid
                    && localValid
                    && string.Equals(remote, local, StringComparison.OrdinalIgnoreCase)
                );

                return new DigestComparison
                {
                    RemoteDigest = remote,
                    LocalDigest = local,
                    ImageRef = imageRef,
                    ChangesNeeded = changesNeeded
                };
            });
    }
}

// Helper classes
public class DigestComparison
{
    public string RemoteDigest { get; set; } = "";
    public string LocalDigest { get; set; } = "";
    public string ImageRef { get; set; } = "";
    public bool ChangesNeeded { get; set; }
}

// public class ImageDeploymentResult
// {
//     public string? ImageUrl { get; set; }
//     public string? ImageTag { get; set; }
//     public Image? Image { get; set; }
// }
