using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Koyeb;

/// <summary>
/// Scale-to-zero light and deep sleep delays of a Koyeb service
/// (<c>crosscloud-koyeb:index:ServiceSleep</c>). It adds the delays to the service's latest
/// definition, redeploys without rebuilding and waits for the deployment. Pass the service's
/// <c>Version</c> in <see cref="KoyebServiceSleepArgs.Triggers"/> so the delays are applied again
/// after the bridged <c>koyeb</c> provider redeploys the service. Deleting it leaves the service
/// unchanged. Served by the native <c>crosscloud-koyeb</c> provider, because the
/// Terraform-bridged <c>koyeb</c> provider cannot set sleep delays.
/// </summary>
public class KoyebServiceSleep : CustomResource
{
    public const string TypeToken = "crosscloud-koyeb:index:ServiceSleep";

    [Output("serviceId")]
    public Output<string> ServiceId { get; private set; } = null!;

    [Output("lightSleepDelaySeconds")]
    public Output<int?> LightSleepDelaySeconds { get; private set; } = null!;

    [Output("deepSleepDelaySeconds")]
    public Output<int?> DeepSleepDelaySeconds { get; private set; } = null!;

    [Output("triggers")]
    public Output<ImmutableArray<string>> Triggers { get; private set; } = null!;

    /// <summary>
    /// The deployment that carries the delays.
    /// </summary>
    [Output("deploymentId")]
    public Output<string> DeploymentId { get; private set; } = null!;

    public KoyebServiceSleep(
        string name,
        KoyebServiceSleepArgs args,
        CustomResourceOptions? options = null
    )
        : base(TypeToken, name, args, CrossCloudPlugins.WithDefaults(options)) { }
}

public sealed class KoyebServiceSleepArgs : ResourceArgs
{
    [Input("serviceId", required: true)]
    public Input<string> ServiceId { get; set; } = null!;

    /// <summary>
    /// Idle seconds before light sleep; 0 disables light sleep.
    /// </summary>
    [Input("lightSleepDelaySeconds")]
    public Input<int>? LightSleepDelaySeconds { get; set; }

    /// <summary>
    /// Idle seconds before deep sleep; 0 disables deep sleep.
    /// </summary>
    [Input("deepSleepDelaySeconds")]
    public Input<int>? DeepSleepDelaySeconds { get; set; }

    private InputList<string>? _triggers;

    /// <summary>
    /// Values that apply the delays again when they change, such as the service's version.
    /// </summary>
    [Input("triggers")]
    public InputList<string> Triggers
    {
        get => _triggers ??= [];
        set => _triggers = value;
    }
}

/// <summary>
/// An explicit <c>crosscloud-koyeb</c> provider. Without one, resources use the default provider
/// configured by <c>crosscloud-koyeb:apiToken</c> stack config (or <c>KOYEB_TOKEN</c>).
/// </summary>
public class CrossCloudKoyebProvider : ProviderResource
{
    public const string Package = "crosscloud-koyeb";

    public CrossCloudKoyebProvider(
        string name,
        CrossCloudKoyebProviderArgs? args = null,
        CustomResourceOptions? options = null
    )
        : base(
            Package,
            name,
            args ?? new CrossCloudKoyebProviderArgs(),
            CrossCloudPlugins.WithDefaults(options)
        ) { }
}

public sealed class CrossCloudKoyebProviderArgs : ResourceArgs
{
    private Input<string>? _apiToken;

    /// <summary>
    /// Koyeb API token. Stored as a secret.
    /// </summary>
    [Input("apiToken")]
    public Input<string>? ApiToken
    {
        get => _apiToken;
        set
        {
            if (value is null)
            {
                _apiToken = null;
                return;
            }

            _apiToken = Output.CreateSecret(value.Apply(token => token));
        }
    }
}
