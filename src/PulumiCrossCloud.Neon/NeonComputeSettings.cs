using System.Globalization;
using Pulumi;
using Pulumi.Neon;

namespace PulumiCrossCloud.Neon;

internal static class NeonComputeSettings
{
    internal static void Validate(NeonPostgresOptions options)
    {
        if (options.MinCu is < 0.25 || options.MaxCu is < 0.25 || options.MinCu > options.MaxCu)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Neon compute requires 0.25 <= MinCu <= MaxCu."
            );
        if (options.SuspendTimeoutSeconds is < -1 or > 604800)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Neon suspend timeout must be -1 through 604800 seconds."
            );
    }

    internal static NeonEndpointSettings? Create(
        string name,
        Project project,
        NeonPostgresOptions options,
        Pulumi.Neon.Provider? provider
    )
    {
        if (options.MinCu is null && options.MaxCu is null && options.SuspendTimeoutSeconds is null)
            return null;

        // Project defaultEndpointSettings describes defaults for new endpoints, not the live
        // endpoint, and the pinned provider drops -1 from those defaults. The native
        // crosscloud-neon provider updates the implicit endpoint in place; it never creates a
        // duplicate read/write compute or replaces a project's database.
        var apiKey = provider is null
            ? new Pulumi.Config("neon").RequireSecret("apiKey")
            : provider.ApiKey.Apply(value =>
                value
                ?? throw new InvalidOperationException(
                    "Neon compute settings require an explicit API key."
                )
            );
        var settingsProvider = new CrossCloudNeonProvider(
            $"{name}-compute-settings-provider",
            new CrossCloudNeonProviderArgs { ApiKey = apiKey }
        );

        return new NeonEndpointSettings(
            $"{name}-compute-settings",
            new NeonEndpointSettingsArgs
            {
                ProjectId = project.Id,
                BranchId = project.DefaultBranchId,
                MinCu = options.MinCu,
                MaxCu = options.MaxCu,
                SuspendTimeoutSeconds = options.SuspendTimeoutSeconds,
            },
            new CustomResourceOptions { Provider = settingsProvider }
        );
    }

    /// <summary>
    /// The applied settings as JSON, in the format earlier releases printed, for example
    /// <c>{"autoscaling_limit_max_cu": 0.25, "autoscaling_limit_min_cu": 0.25, "endpointId": "ep-1", "suspend_timeout_seconds": 300}</c>.
    /// </summary>
    internal static Output<string> Describe(
        NeonEndpointSettings settings,
        NeonPostgresOptions options
    ) =>
        settings.EndpointId.Apply(endpointId =>
        {
            // Keys sorted, matching the previous script's json.dumps(..., sort_keys=True).
            List<string> fields = [];
            if (options.MaxCu is { } maxCu)
                fields.Add($"\"autoscaling_limit_max_cu\": {FormatFloat(maxCu)}");
            if (options.MinCu is { } minCu)
                fields.Add($"\"autoscaling_limit_min_cu\": {FormatFloat(minCu)}");
            fields.Add($"\"endpointId\": \"{endpointId}\"");
            if (options.SuspendTimeoutSeconds is { } suspend)
                fields.Add(
                    $"\"suspend_timeout_seconds\": {(suspend == 0 ? 300 : suspend).ToString(CultureInfo.InvariantCulture)}"
                );
            return $"{{{string.Join(", ", fields)}}}";
        });

    // Python prints whole floats with a trailing .0.
    private static string FormatFloat(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : $"{text}.0";
    }
}
