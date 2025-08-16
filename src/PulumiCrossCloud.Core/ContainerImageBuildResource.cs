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

        // 1) Remote digest of :latest
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

        // 2) Local digest for current Docker context (no push). Build temp image, get Id via jq, then remove temp image
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

        // 3) Decide whether to build/push based on digest equality
        var imageResult = Output
            .Tuple(remoteDigest, localDigest, latestImageRef)
            .Apply(tuple =>
            {
                var (remote, local, latestRef) = tuple;

                bool remoteValid =
                    !string.IsNullOrWhiteSpace(remote)
                    && remote != "no-manifest"
                    && remote != "null";
                bool localValid = !string.IsNullOrWhiteSpace(local);

                if (
                    remoteValid
                    && localValid
                    && string.Equals(remote, local, StringComparison.OrdinalIgnoreCase)
                )
                {
                    Log.Info(
                        $"Image digests match ({local[..Math.Min(12, local.Length)]}) - skipping Docker build/push for {latestRef}"
                    );
                    return new ImageDeploymentResult
                    {
                        ImageUrl = latestRef,
                        ImageTag = "latest",
                        Image = null
                    };
                }

                Log.Info(
                    $"Digest differs or remote missing (remote='{remote}', local='{local}') - building and pushing {latestRef}..."
                );
                var dockerImage = new Image(
                    $"{registry.Name}-image-latest",
                    imageArgs
                        ?? new ImageArgs
                        {
                            Tags = [latestRef],
                            Context = new BuildContextArgs { Location = contextPath },
                            Dockerfile = new DockerfileArgs { Location = dockerfilePath },
                            CacheTo = [new CacheToArgs { Inline = null }],
                            CacheFrom =
                            [
                                new CacheFromArgs
                                {
                                    Registry = new CacheFromRegistryArgs { Ref = latestRef }
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
                    ImageUrl = latestRef,
                    ImageTag = "latest",
                    Image = dockerImage
                };
            });

        ImageUrl = imageResult.Apply(r => r.ImageUrl ?? "");
        ImageTag = imageResult.Apply(r => r.ImageTag ?? "");
        ImageResource = imageResult.Apply(r => r.Image);
    }
}
