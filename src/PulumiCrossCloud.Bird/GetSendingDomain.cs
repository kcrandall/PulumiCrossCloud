using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// Looks up an existing Bird sending domain by name (<c>crosscloud-bird:index:getSendingDomain</c>),
/// for example to publish its DNS records from a stack that does not own the domain.
/// </summary>
public static class GetSendingDomain
{
    public const string Token = "crosscloud-bird:index:getSendingDomain";

    public static Task<GetSendingDomainResult> InvokeAsync(
        GetSendingDomainArgs args,
        InvokeOptions? options = null
    ) =>
        Deployment.Instance.InvokeAsync<GetSendingDomainResult>(
            Token,
            args,
            CrossCloudPlugins.WithDefaults(options)
        );

    public static Output<GetSendingDomainResult> Invoke(
        GetSendingDomainInvokeArgs args,
        InvokeOptions? options = null
    ) =>
        Deployment.Instance.Invoke<GetSendingDomainResult>(
            Token,
            args,
            CrossCloudPlugins.WithDefaults(options)
        );
}

public sealed class GetSendingDomainArgs : InvokeArgs
{
    [Input("domain", required: true)]
    public string Domain { get; set; } = null!;
}

public sealed class GetSendingDomainInvokeArgs : InvokeArgs
{
    [Input("domain", required: true)]
    public Input<string> Domain { get; set; } = null!;
}

[OutputType]
public sealed class GetSendingDomainResult
{
    /// <summary>The Bird domain ID (<c>dom_…</c>).</summary>
    public readonly string Id;
    public readonly string Domain;
    public readonly string Region;
    public readonly string? WorkspaceId;
    public readonly string Status;
    public readonly string? SendingStatus;
    public readonly string? ReturnPathName;
    public readonly string? ReturnPathDomain;
    public readonly string? TrackingName;
    public readonly string? TrackingDomain;
    public readonly bool? ClickTracking;
    public readonly bool? OpenTracking;
    public readonly string? DefaultIpPoolId;
    public readonly string? DkimSelector;
    public readonly string? VerifiedAt;
    public readonly string? ReceivingState;
    public readonly string? InboundStatus;
    public readonly ImmutableArray<SendingDomainDnsRecord> DnsRecords;

    [OutputConstructor]
    public GetSendingDomainResult(
        string id,
        string domain,
        string region,
        string? workspaceId,
        string status,
        string? sendingStatus,
        string? returnPathName,
        string? returnPathDomain,
        string? trackingName,
        string? trackingDomain,
        bool? clickTracking,
        bool? openTracking,
        string? defaultIpPoolId,
        string? dkimSelector,
        string? verifiedAt,
        string? receivingState,
        string? inboundStatus,
        ImmutableArray<SendingDomainDnsRecord> dnsRecords
    )
    {
        Id = id;
        Domain = domain;
        Region = region;
        WorkspaceId = workspaceId;
        Status = status;
        SendingStatus = sendingStatus;
        ReturnPathName = returnPathName;
        ReturnPathDomain = returnPathDomain;
        TrackingName = trackingName;
        TrackingDomain = trackingDomain;
        ClickTracking = clickTracking;
        OpenTracking = openTracking;
        DefaultIpPoolId = defaultIpPoolId;
        DkimSelector = dkimSelector;
        VerifiedAt = verifiedAt;
        ReceivingState = receivingState;
        InboundStatus = inboundStatus;
        DnsRecords = dnsRecords;
    }
}
