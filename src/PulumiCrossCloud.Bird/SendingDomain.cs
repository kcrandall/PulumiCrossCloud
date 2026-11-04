using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// A Bird email sending domain (<c>crosscloud-bird:index:SendingDomain</c>). Publish the records in
/// <see cref="DnsRecords"/>, then use <see cref="DomainVerification"/> to wait until it can send.
/// Changing <see cref="SendingDomainArgs.Domain"/> replaces the domain.
/// </summary>
public class SendingDomain : CustomResource
{
    public const string TypeToken = "crosscloud-bird:index:SendingDomain";

    [Output("domain")]
    public Output<string> Domain { get; private set; } = null!;

    [Output("region")]
    public Output<string> Region { get; private set; } = null!;

    [Output("workspaceId")]
    public Output<string?> WorkspaceId { get; private set; } = null!;

    /// <summary>
    /// Ownership verification: pending, verified, failed, temporary_failure or rejected.
    /// </summary>
    [Output("status")]
    public Output<string> Status { get; private set; } = null!;

    /// <summary>
    /// Whether the domain may send: pending, verified, warning, failed or temporary_failure.
    /// </summary>
    [Output("sendingStatus")]
    public Output<string?> SendingStatus { get; private set; } = null!;

    [Output("returnPathName")]
    public Output<string> ReturnPathName { get; private set; } = null!;

    [Output("returnPathDomain")]
    public Output<string?> ReturnPathDomain { get; private set; } = null!;

    /// <summary>
    /// The tracking name part, or an empty string when tracking is disabled.
    /// </summary>
    [Output("trackingName")]
    public Output<string> TrackingName { get; private set; } = null!;

    [Output("trackingDomain")]
    public Output<string?> TrackingDomain { get; private set; } = null!;

    [Output("clickTracking")]
    public Output<bool> ClickTracking { get; private set; } = null!;

    [Output("openTracking")]
    public Output<bool> OpenTracking { get; private set; } = null!;

    /// <summary>
    /// The default IP pool, or an empty string for normal organization routing.
    /// </summary>
    [Output("defaultIpPoolId")]
    public Output<string> DefaultIpPoolId { get; private set; } = null!;

    /// <summary>
    /// Receiving control state: enabled, disabling, disable_failed or disabled.
    /// </summary>
    [Output("receivingState")]
    public Output<string?> ReceivingState { get; private set; } = null!;

    /// <summary>
    /// Inbound readiness: not_configured until receiving is enabled, then pending or verified.
    /// </summary>
    [Output("inboundStatus")]
    public Output<string?> InboundStatus { get; private set; } = null!;

    [Output("dkimSelector")]
    public Output<string?> DkimSelector { get; private set; } = null!;

    [Output("verifiedAt")]
    public Output<string?> VerifiedAt { get; private set; } = null!;

    /// <summary>
    /// Every DNS record Bird reports for the domain, including optional and deprecated ones.
    /// </summary>
    [Output("dnsRecords")]
    public Output<ImmutableArray<SendingDomainDnsRecord>> DnsRecords { get; private set; } = null!;

    public SendingDomain(string name, SendingDomainArgs args, CustomResourceOptions? options = null)
        : base(TypeToken, name, args, CrossCloudPlugins.WithDefaults(options)) { }

    private SendingDomain(string name, Input<string> id, CustomResourceOptions? options = null)
        : base(TypeToken, name, null, CrossCloudPlugins.WithDefaults(options, id)) { }

    /// <summary>
    /// Reads an existing sending domain by its Bird ID (<c>dom_…</c>).
    /// </summary>
    public static SendingDomain Get(
        string name,
        Input<string> id,
        CustomResourceOptions? options = null
    ) => new(name, id, options);
}

public sealed class SendingDomainArgs : ResourceArgs
{
    /// <summary>
    /// The domain of your from addresses. Prefer a dedicated subdomain such as mail.example.com.
    /// </summary>
    [Input("domain", required: true)]
    public Input<string> Domain { get; set; } = null!;

    /// <summary>
    /// Return-path (bounce) name part, for example <c>send</c> for send.mail.example.com. Bird defaults to <c>send</c>.
    /// </summary>
    [Input("returnPathName")]
    public Input<string>? ReturnPathName { get; set; }

    /// <summary>
    /// Branded tracking name part. Bird defaults to <c>links</c>; an empty string disables tracking.
    /// </summary>
    [Input("trackingName")]
    public Input<string>? TrackingName { get; set; }

    [Input("clickTracking")]
    public Input<bool>? ClickTracking { get; set; }

    [Input("openTracking")]
    public Input<bool>? OpenTracking { get; set; }

    /// <summary>
    /// Default IP pool (<c>ipp_…</c>) for sends that omit one; an empty string uses normal organization routing.
    /// </summary>
    [Input("defaultIpPoolId")]
    public Input<string>? DefaultIpPoolId { get; set; }
}

/// <summary>
/// A DNS record reported by Bird for a sending domain.
/// </summary>
[OutputType]
public sealed class SendingDomainDnsRecord
{
    /// <summary>TXT, CNAME or MX.</summary>
    public readonly string Type;

    /// <summary>The record name relative to the registered domain's zone.</summary>
    public readonly string Name;

    /// <summary>The fully qualified record name.</summary>
    public readonly string Host;

    /// <summary>The record content; MX values are the priority followed by the host.</summary>
    public readonly string Value;

    /// <summary>dkim, return_path, dmarc, tracking or inbound_mx.</summary>
    public readonly string Purpose;

    /// <summary>active, pending (a staged change) or deprecated.</summary>
    public readonly string State;

    public readonly bool Optional;

    /// <summary>pending, verified, warning or failed.</summary>
    public readonly string Status;

    [OutputConstructor]
    public SendingDomainDnsRecord(
        string type,
        string name,
        string host,
        string value,
        string purpose,
        string state,
        bool optional,
        string status
    )
    {
        Type = type;
        Name = name;
        Host = host;
        Value = value;
        Purpose = purpose;
        State = state;
        Optional = optional;
        Status = status;
    }
}
