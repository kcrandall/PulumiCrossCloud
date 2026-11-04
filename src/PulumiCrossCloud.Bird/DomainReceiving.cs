using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// Enables inbound mail on a sending domain (<c>crosscloud-bird:index:DomainReceiving</c>). Bird
/// requires the domain's DKIM record to be verified first, so make it depend on
/// <see cref="DomainVerification"/>, and publish the domain's <c>inbound_mx</c> records. Deleting
/// it turns receiving off and waits until Bird reports it off (default 10 minutes, or
/// <c>CustomTimeouts.Delete</c>), so the domain can then be deleted.
/// </summary>
public class DomainReceiving : CustomResource
{
    public const string TypeToken = "crosscloud-bird:index:DomainReceiving";

    [Output("domainId")]
    public Output<string> DomainId { get; private set; } = null!;

    /// <summary>
    /// enabled, disabling, disable_failed or disabled.
    /// </summary>
    [Output("receivingState")]
    public Output<string?> ReceivingState { get; private set; } = null!;

    /// <summary>
    /// pending until the MX records resolve to Bird, then verified.
    /// </summary>
    [Output("inboundStatus")]
    public Output<string?> InboundStatus { get; private set; } = null!;

    public DomainReceiving(
        string name,
        DomainReceivingArgs args,
        CustomResourceOptions? options = null
    )
        : base(TypeToken, name, args, CrossCloudPlugins.WithDefaults(options)) { }
}

public sealed class DomainReceivingArgs : ResourceArgs
{
    /// <summary>
    /// The sending domain's Bird ID, usually <see cref="SendingDomain"/>'s <c>Id</c>.
    /// </summary>
    [Input("domainId", required: true)]
    public Input<string> DomainId { get; set; } = null!;
}
