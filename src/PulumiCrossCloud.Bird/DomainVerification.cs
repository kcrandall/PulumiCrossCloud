using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// Runs Bird's DNS check for a sending domain (<c>crosscloud-bird:index:DomainVerification</c>) and, by
/// default, waits until the domain may send. Make it depend on the DNS record resources.
/// The wait honors <c>CustomTimeouts.Create</c> and defaults to 20 minutes.
/// Deleting it has no effect in Bird.
/// </summary>
public class DomainVerification : CustomResource
{
    public const string TypeToken = "crosscloud-bird:index:DomainVerification";

    [Output("domainId")]
    public Output<string> DomainId { get; private set; } = null!;

    [Output("waitForSending")]
    public Output<bool> WaitForSending { get; private set; } = null!;

    [Output("triggers")]
    public Output<ImmutableArray<string>> Triggers { get; private set; } = null!;

    [Output("status")]
    public Output<string> Status { get; private set; } = null!;

    [Output("sendingStatus")]
    public Output<string?> SendingStatus { get; private set; } = null!;

    [Output("verifiedAt")]
    public Output<string?> VerifiedAt { get; private set; } = null!;

    public DomainVerification(
        string name,
        DomainVerificationArgs args,
        CustomResourceOptions? options = null
    )
        : base(TypeToken, name, args, CrossCloudPlugins.WithDefaults(options)) { }
}

public sealed class DomainVerificationArgs : ResourceArgs
{
    /// <summary>
    /// The sending domain's Bird ID, usually <see cref="SendingDomain"/>'s <c>Id</c>.
    /// </summary>
    [Input("domainId", required: true)]
    public Input<string> DomainId { get; set; } = null!;

    /// <summary>
    /// Wait until the domain's sending capability is verified. Defaults to true.
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
