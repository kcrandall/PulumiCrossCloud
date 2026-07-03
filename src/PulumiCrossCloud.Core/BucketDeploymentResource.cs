using System.Security.Cryptography;
using System.Text;
using Pulumi;
using Pulumi.Command.Local;

namespace PulumiCrossCloud.Core;

/// <summary>
/// A base resource for managing bucket deployments with hash-based change detection.
/// </summary>
public abstract class BucketDeploymentResource : ComponentResource
{
    public Output<string> ContentHash { get; }
    public Resource? BuildResource { get; }

    protected BucketDeploymentResource(string type, string name, string localPath, BucketSyncOptions? options = null, ComponentResourceOptions? resourceOptions = null)
        : base(type, name, resourceOptions)
    {
        // Hash the source directory (inputs) or the output directory
        var hashPath = options?.SourcePath ?? localPath;
        var hash = GenerateDirectoryHash(hashPath);
        ContentHash = Output.Create(hash);

        // Optional Build Step
        if (options != null && !string.IsNullOrEmpty(options.BuildCommand))
        {
            var buildCwd = options.BuildCwd;
            if (string.IsNullOrEmpty(buildCwd))
            {
                var fullPath = Path.GetFullPath(hashPath);
                buildCwd = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            }

            var cmd = options.BuildCommand;
            var build = new Command($"{name}-build", new CommandArgs
            {
                Create = cmd,
                Update = cmd,
                Dir = buildCwd,
                Triggers = new InputList<string> { (Input<string>)ContentHash }
            }, new CustomResourceOptions { Parent = this });
            
            BuildResource = build;
        }
    }

    private static string GenerateDirectoryHash(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        using var sha256 = SHA256.Create();

        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(path, file).Replace('\\', '/');
            var relBytes = Encoding.UTF8.GetBytes(rel);
            sha256.TransformBlock(relBytes, 0, relBytes.Length, null, 0);

            var bytes = File.ReadAllBytes(file);
            sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLower().Substring(0, 12);
    }
}
