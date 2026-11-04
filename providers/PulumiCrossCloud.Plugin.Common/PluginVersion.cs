using System.Reflection;

namespace PulumiCrossCloud.Plugin.Common;

public static class PluginVersion
{
    /// <summary>
    /// The plugin's SemVer version: the lockstep package version without build metadata.
    /// </summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var informational = typeof(PluginVersion)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
            return "0.0.0";

        var metadata = informational.IndexOf('+');
        return metadata < 0 ? informational : informational[..metadata];
    }
}
