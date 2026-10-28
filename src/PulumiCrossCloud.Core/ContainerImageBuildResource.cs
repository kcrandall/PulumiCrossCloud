using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Pulumi;
using Pulumi.DockerBuild;
using Pulumi.DockerBuild.Inputs;

namespace PulumiCrossCloud.Core;

public class ContainerImageBuildResource : ComponentResource
{
    public Image Resource { get; }
    public Output<Image> ResourceOutput { get; }
    public Output<string> ImageUrl { get; }
    public Output<string> ImageTag { get; }

    public ContainerImageBuildResource(
        string name,
        IContainerRegistry registry,
        string imageName,
        string dockerfilePath,
        string contextPath,
        ImageArgs? imageArgs = null,
        bool appendImageNameToRegistryUrl = true
    )
        : base("crosscloud:core:ContainerImageBuildResource", name)
    {
        var contentHash = GenerateContentHash(contextPath, dockerfilePath);
        var imageTag = $"sha-{contentHash}";

        var imageRepositoryUrl = registry.RegistryUrlOutput.Apply(url =>
            appendImageNameToRegistryUrl ? $"{url.TrimEnd('/')}/{imageName}" : url.TrimEnd('/')
        );
        var shaRef = imageRepositoryUrl.Apply(url => $"{url}:{imageTag}");
        var latestRef = imageRepositoryUrl.Apply(url => $"{url}:latest");

        var args =
            imageArgs
            ?? new ImageArgs
            {
                Tags = [shaRef, latestRef],

                Context = new BuildContextArgs { Location = contextPath },
                Dockerfile = new DockerfileArgs { Location = dockerfilePath },

                // Make caching stable/predictable
                CacheTo = [new CacheToArgs { Inline = new CacheToInlineArgs { } }],
                CacheFrom =
                [
                    // pull cache from latest (warm start) and the sha tag itself
                    new CacheFromArgs { Registry = new CacheFromRegistryArgs { Ref = latestRef } },
                    // new CacheFromArgs { Registry = new CacheFromRegistryArgs { Ref = shaRef } },
                ],

                Platforms = [Platform.Linux_amd64],
                Push = true,
                Registries = registry.GetDockerBuildRegistryConfig(),
            };

        if (imageArgs is not null)
        {
            // PushImage owns the source, destination tags, and destination credentials. Keep
            // the caller's other advanced build settings, but make the reported ImageUrl a tag
            // that this resource actually builds and pushes.
            args.Context = new BuildContextArgs { Location = contextPath };
            args.Dockerfile = new DockerfileArgs { Location = dockerfilePath };
            args.Tags = [shaRef, latestRef];
            args.Push = true;
            args.Registries = registry.GetDockerBuildRegistryConfig();
        }

        // Keep providers in the deployment that owns them. A static provider can refer to
        // a previous deployment, and per-build names allow multiple images per registry.
        var provider = new Pulumi.DockerBuild.Provider(
            $"{name}-docker-build",
            new Pulumi.DockerBuild.ProviderArgs { },
            new CustomResourceOptions { Parent = this }
        );

        var dockerImage = new Image(
            $"{name}-image",
            args,
            new CustomResourceOptions
            {
                Parent = this,
                DependsOn = [registry.Resource],
                Provider = provider,
            }
        );

        Resource = dockerImage;
        // Build settings and base images can change the image without changing
        // its source tag. Pin consumers to the exported image digest.
        ImageUrl = Output
            .Tuple(shaRef, dockerImage.Digest)
            .Apply(result =>
                string.IsNullOrEmpty(result.Item2) ? result.Item1 : $"{result.Item1}@{result.Item2}"
            );
        ImageTag = Output.Create(imageTag);
        ResourceOutput = Output.Create(dockerImage);
    }

    private string GenerateContentHash(string contextPath, string dockerfilePath)
    {
        var dockerIgnorePatterns = LoadDockerIgnorePatterns(contextPath, dockerfilePath);

        var files = Directory
            .GetFiles(contextPath, "*", SearchOption.AllDirectories)
            .Where(f => !IsIgnoredByDockerIgnore(f, contextPath, dockerIgnorePatterns))
            .OrderBy(f => f, StringComparer.Ordinal) // deterministic ordering
            .ToList();

        using var sha256 = SHA256.Create();

        // Hash the Dockerfile content first (if present)
        if (File.Exists(dockerfilePath))
        {
            var dockerfileBytes = File.ReadAllBytes(dockerfilePath);
            ContentHashBuilder.AppendBlock(sha256, dockerfileBytes);
        }

        // Hash each file's relative path + bytes
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(contextPath, file).Replace('\\', '/');
            var relBytes = Encoding.UTF8.GetBytes(rel);
            ContentHashBuilder.AppendBlock(sha256, relBytes);

            var bytes = File.ReadAllBytes(file);
            ContentHashBuilder.AppendBlock(sha256, bytes);
        }

        // finalize
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash!)[..8].ToLower();
    }

    private List<string> LoadDockerIgnorePatterns(string contextPath, string dockerfilePath)
    {
        var dockerIgnorePath = File.Exists($"{dockerfilePath}.dockerignore")
            ? $"{dockerfilePath}.dockerignore"
            : Path.Combine(contextPath, ".dockerignore");
        var patterns = new List<string>();
        if (File.Exists(dockerIgnorePath))
            foreach (var line in File.ReadAllLines(dockerIgnorePath))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                    patterns.Add(trimmed);
            }

        return patterns;
    }

    private bool IsIgnoredByDockerIgnore(string filePath, string contextPath, List<string> patterns)
    {
        var relativePath = Path.GetRelativePath(contextPath, filePath).Replace('\\', '/');
        var ignored = false;
        foreach (var pattern in patterns)
        {
            var include = pattern.StartsWith('!');
            if (MatchesPattern(relativePath, include ? pattern[1..] : pattern))
                ignored = !include;
        }
        return ignored;
    }

    private bool MatchesPattern(string path, string pattern)
    {
        pattern = pattern.Replace('\\', '/').Trim('/');
        while (pattern.StartsWith("./", StringComparison.Ordinal))
            pattern = pattern[2..];
        if (pattern.Length == 0 || pattern == ".")
            return false;

        var regex = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                    {
                        regex.Append("(?:.*/)?");
                        i++;
                    }
                    else
                        regex.Append(".*");
                }
                else
                    regex.Append("[^/]*");
            }
            else if (pattern[i] == '?')
                regex.Append("[^/]");
            else
                regex.Append(Regex.Escape(pattern[i].ToString()));
        }
        regex.Append("(?:/.*)?$");
        return Regex.IsMatch(path, regex.ToString(), RegexOptions.NonBacktracking);
    }

    private bool IsSourceFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        return ext
            is ".cs"
                or ".csproj"
                or ".sln"
                or ".json"
                or ".yml"
                or ".yaml"
                or ".xml"
                or ".props"
                or ".targets";
    }
}
