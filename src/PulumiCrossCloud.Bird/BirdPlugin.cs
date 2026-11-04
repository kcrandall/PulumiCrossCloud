using System.Reflection;
using Pulumi;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// Locates the <c>pulumi-resource-bird</c> plugin. Each PulumiCrossCloud release publishes
/// the plugin as GitHub release assets with the same version as this package, so Pulumi
/// downloads it automatically. For local plugin builds, point the project's
/// <c>Pulumi.yaml</c> <c>plugins.providers</c> entry at the build output instead.
/// </summary>
public static class BirdPlugin
{
    public const string Name = "bird";

    public const string DownloadUrl = "github://api.github.com/kcrandall/PulumiCrossCloud";

    /// <summary>
    /// The plugin version these resources request: this package's version without build metadata.
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    internal static CustomResourceOptions WithDefaults(
        CustomResourceOptions? options,
        Input<string>? id = null
    )
    {
        var merged = CustomResourceOptions.Merge(
            new CustomResourceOptions { Version = Version, PluginDownloadURL = DownloadUrl },
            options
        );
        merged.Id = id ?? merged.Id;
        return merged;
    }

    private static string ResolveVersion()
    {
        var informational = typeof(BirdPlugin)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
            return "0.0.0";

        var metadata = informational.IndexOf('+');
        return metadata < 0 ? informational : informational[..metadata];
    }
}
