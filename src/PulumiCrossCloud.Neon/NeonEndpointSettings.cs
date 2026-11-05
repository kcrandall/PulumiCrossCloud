using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Neon;

/// <summary>
/// Autoscaling limits and suspend timeout of a branch's existing read/write compute
/// (<c>crosscloud-neon:index:EndpointSettings</c>). Only the settings given are managed. It never
/// creates or deletes a compute, and deleting it leaves the compute's settings unchanged. Served
/// by the native <c>crosscloud-neon</c> provider, because the Terraform-bridged <c>neon</c>
/// provider only sets project defaults for new computes.
/// </summary>
public class NeonEndpointSettings : CustomResource
{
    public const string TypeToken = "crosscloud-neon:index:EndpointSettings";

    [Output("projectId")]
    public Output<string> ProjectId { get; private set; } = null!;

    [Output("branchId")]
    public Output<string> BranchId { get; private set; } = null!;

    [Output("endpointId")]
    public Output<string> EndpointId { get; private set; } = null!;

    [Output("minCu")]
    public Output<double> MinCu { get; private set; } = null!;

    [Output("maxCu")]
    public Output<double> MaxCu { get; private set; } = null!;

    [Output("suspendTimeoutSeconds")]
    public Output<int> SuspendTimeoutSeconds { get; private set; } = null!;

    public NeonEndpointSettings(
        string name,
        NeonEndpointSettingsArgs args,
        CustomResourceOptions? options = null
    )
        : base(TypeToken, name, args, CrossCloudPlugins.WithDefaults(options)) { }
}

public sealed class NeonEndpointSettingsArgs : ResourceArgs
{
    [Input("projectId", required: true)]
    public Input<string> ProjectId { get; set; } = null!;

    [Input("branchId", required: true)]
    public Input<string> BranchId { get; set; } = null!;

    [Input("minCu")]
    public Input<double>? MinCu { get; set; }

    [Input("maxCu")]
    public Input<double>? MaxCu { get; set; }

    /// <summary>
    /// -1 never suspends; 0 uses Neon's default (five minutes).
    /// </summary>
    [Input("suspendTimeoutSeconds")]
    public Input<int>? SuspendTimeoutSeconds { get; set; }
}

/// <summary>
/// An explicit <c>crosscloud-neon</c> provider. Without one, resources use the default provider
/// configured by <c>crosscloud-neon:apiKey</c> stack config (or <c>NEON_API_KEY</c>).
/// </summary>
public class CrossCloudNeonProvider : ProviderResource
{
    public const string Package = "crosscloud-neon";

    public CrossCloudNeonProvider(
        string name,
        CrossCloudNeonProviderArgs? args = null,
        CustomResourceOptions? options = null
    )
        : base(
            Package,
            name,
            args ?? new CrossCloudNeonProviderArgs(),
            CrossCloudPlugins.WithDefaults(options)
        ) { }
}

public sealed class CrossCloudNeonProviderArgs : ResourceArgs
{
    private Input<string>? _apiKey;

    /// <summary>
    /// Neon API key. Stored as a secret.
    /// </summary>
    [Input("apiKey")]
    public Input<string>? ApiKey
    {
        get => _apiKey;
        set
        {
            if (value is null)
            {
                _apiKey = null;
                return;
            }

            _apiKey = Output.CreateSecret(value.Apply(key => key));
        }
    }
}
