using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Mailgun;

/// <summary>
/// Runs Mailgun's DNS check for a domain (<c>crosscloud-mailgun:index:DomainVerification</c>) and,
/// by default, waits until the domain is active. Make it depend on the DNS record resources. The
/// wait honors <c>CustomTimeouts.Create</c> and defaults to 20 minutes. Deleting it has no effect
/// in Mailgun. Served by the native <c>crosscloud-mailgun</c> provider, because the
/// Terraform-bridged <c>mailgun</c> provider has no verification resource.
/// </summary>
public class MailgunDomainVerification : CustomResource
{
    public const string TypeToken = "crosscloud-mailgun:index:DomainVerification";

    [Output("domain")]
    public Output<string> Domain { get; private set; } = null!;

    [Output("waitForSending")]
    public Output<bool> WaitForSending { get; private set; } = null!;

    [Output("triggers")]
    public Output<ImmutableArray<string>> Triggers { get; private set; } = null!;

    /// <summary>
    /// active, unverified or disabled.
    /// </summary>
    [Output("state")]
    public Output<string> State { get; private set; } = null!;

    public MailgunDomainVerification(
        string name,
        MailgunDomainVerificationArgs args,
        CustomResourceOptions? options = null
    )
        : base(TypeToken, name, args, CrossCloudPlugins.WithDefaults(options)) { }
}

public sealed class MailgunDomainVerificationArgs : ResourceArgs
{
    [Input("domain", required: true)]
    public Input<string> Domain { get; set; } = null!;

    /// <summary>
    /// Wait until the domain is active. Defaults to true.
    /// </summary>
    [Input("waitForSending")]
    public Input<bool>? WaitForSending { get; set; }

    private InputList<string>? _triggers;

    /// <summary>
    /// Values that re-run verification when they change, such as the published record values.
    /// </summary>
    [Input("triggers")]
    public InputList<string> Triggers
    {
        get => _triggers ??= [];
        set => _triggers = value;
    }
}

/// <summary>
/// An explicit <c>crosscloud-mailgun</c> provider. Without one, verification uses the default
/// provider configured by <c>crosscloud-mailgun:apiKey</c>/<c>crosscloud-mailgun:region</c> stack
/// config (or <c>MAILGUN_API_KEY</c>/<c>MAILGUN_REGION</c>).
/// </summary>
public class CrossCloudMailgunProvider : ProviderResource
{
    public const string Package = "crosscloud-mailgun";

    public CrossCloudMailgunProvider(
        string name,
        CrossCloudMailgunProviderArgs? args = null,
        CustomResourceOptions? options = null
    )
        : base(
            Package,
            name,
            args ?? new CrossCloudMailgunProviderArgs(),
            CrossCloudPlugins.WithDefaults(options)
        ) { }
}

public sealed class CrossCloudMailgunProviderArgs : ResourceArgs
{
    private Input<string>? _apiKey;

    /// <summary>
    /// Mailgun API key. Stored as a secret.
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

    /// <summary>
    /// <c>us</c> (default) or <c>eu</c>.
    /// </summary>
    [Input("region")]
    public Input<string>? Region { get; set; }
}
