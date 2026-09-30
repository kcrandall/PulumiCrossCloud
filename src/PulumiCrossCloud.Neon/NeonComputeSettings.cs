using System.Globalization;
using Pulumi;
using Pulumi.Command.Local;
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

    internal static Command? Create(
        string name,
        Project project,
        NeonPostgresOptions options,
        Provider? provider
    )
    {
        if (options.MinCu is null && options.MaxCu is null && options.SuspendTimeoutSeconds is null)
            return null;

        // Project defaultEndpointSettings describes defaults for new endpoints, not the live
        // endpoint. The pinned provider also drops -1 from those defaults. Update the implicit
        // endpoint in place through a tracked command; never create a duplicate read/write
        // compute or replace a project's database. This requires python3 on the Pulumi runner.
        using var stream =
            typeof(NeonComputeSettings).Assembly.GetManifestResourceStream(
                "PulumiCrossCloud.Neon.configure-neon-compute.py"
            ) ?? throw new InvalidOperationException("Missing embedded Neon compute policy.");
        using var reader = new StreamReader(stream);
        var command =
            $"python3 - <<'NEON_COMPUTE_POLICY'\n{reader.ReadToEnd()}\nNEON_COMPUTE_POLICY";
        var apiKey = provider is null
            ? new Pulumi.Config("neon").RequireSecret("apiKey")
            : provider.ApiKey.Apply(value =>
                value
                ?? throw new InvalidOperationException(
                    "Neon compute settings require an explicit API key."
                )
            );

        return new Command(
            $"{name}-compute-settings",
            new CommandArgs
            {
                Create = command,
                Update = command,
                Environment =
                {
                    ["NEON_API_KEY"] = Output.CreateSecret(apiKey),
                    ["NEON_PROJECT_ID"] = project.Id,
                    ["NEON_BRANCH_ID"] = project.DefaultBranchId,
                    ["NEON_MIN_CU"] = options.MinCu?.ToString(CultureInfo.InvariantCulture) ?? "",
                    ["NEON_MAX_CU"] = options.MaxCu?.ToString(CultureInfo.InvariantCulture) ?? "",
                    ["NEON_SUSPEND_TIMEOUT_SECONDS"] =
                        options.SuspendTimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "",
                },
            },
            new CustomResourceOptions { DependsOn = [project] }
        );
    }
}
