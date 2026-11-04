using System.Reflection;
using Pulumi;

namespace PulumiCrossCloud.Core;

/// <summary>
/// Locates the pulumi-resource-crosscloud-* provider plugins built in this repository. Each
/// release attaches them to the GitHub release that has the same version as these packages, so
/// Pulumi downloads only the plugins a program uses. For local plugin builds, point the
/// project's <c>Pulumi.yaml</c> <c>plugins.providers</c> entries at the build output instead.
/// </summary>
public static class CrossCloudPlugins
{
    public const string DownloadUrl = "github://api.github.com/kcrandall/PulumiCrossCloud";

    /// <summary>
    /// The plugin version resources request: the package version without build metadata.
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>
    /// Adds the plugin version and download location to resource options.
    /// </summary>
    public static CustomResourceOptions WithDefaults(
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

    /// <summary>
    /// Adds the plugin version and download location to invoke options.
    /// </summary>
    public static InvokeOptions WithDefaults(InvokeOptions? options) =>
        new()
        {
            Parent = options?.Parent,
            Provider = options?.Provider,
            Version = options?.Version ?? Version,
            PluginDownloadURL = options?.PluginDownloadURL ?? DownloadUrl,
        };

    private static string ResolveVersion()
    {
        var informational = typeof(CrossCloudPlugins)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
            return "0.0.0";

        var metadata = informational.IndexOf('+');
        return metadata < 0 ? informational : informational[..metadata];
    }
}
