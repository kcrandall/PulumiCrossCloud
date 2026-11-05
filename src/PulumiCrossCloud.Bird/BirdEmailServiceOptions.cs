using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

public sealed record BirdEmailServiceOptions
{
    /// <summary>
    /// Return-path (bounce) name part. Bird defaults to <c>send</c>.
    /// </summary>
    public string? ReturnPathName { get; init; }

    /// <summary>
    /// Branded tracking name part. Null keeps Bird's default (<c>links</c>); an empty string
    /// disables tracking and skips its CNAME record.
    /// </summary>
    public string? TrackingName { get; init; }

    /// <summary>
    /// Publish a DMARC record for the sending domain. Disable when the zone already has a DMARC
    /// policy at the sending domain; a policy on the registered domain also satisfies Bird.
    /// </summary>
    public bool PublishDmarcRecord { get; init; } = true;

    /// <summary>
    /// Wait during deployment until Bird verifies the domain for sending. Defaults to true when
    /// a DNS zone is supplied, because the records are then published by the same deployment.
    /// </summary>
    public bool? WaitForVerification { get; init; }

    /// <summary>
    /// Default IP pool (<c>ipp_…</c>) for sends that omit one. Null leaves Bird's setting alone;
    /// an empty string uses normal organization routing.
    /// </summary>
    public string? DefaultIpPoolId { get; init; }

    /// <summary>
    /// Enable inbound mail on the domain and publish its MX records. Mail to any address at the
    /// domain then arrives as an inbound message. Use a dedicated subdomain: MX records on a
    /// domain that already receives mail would redirect it.
    /// </summary>
    public bool ReceivingEnabled { get; init; }

    /// <summary>
    /// Enable inbound mail (as <see cref="ReceivingEnabled"/> does) and post each message to a
    /// URL through an <c>email.received</c> webhook. Webhook endpoints receive the events of
    /// every domain in the workspace, so handlers should check the recipient domain.
    /// </summary>
    public InboundMailOptions? InboundMail { get; init; }

    /// <summary>
    /// How many inbound MX records Bird lists for the region (3 at the time of writing). Each
    /// gets a stable DNS resource, so the count is fixed up front; deployment fails with the
    /// actual count if Bird reports a different number.
    /// </summary>
    public int InboundMxRecordCount { get; init; } = 3;

    /// <summary>
    /// SMTP submission port: 587 or 2525 (STARTTLS) or 465 (implicit TLS).
    /// </summary>
    public int SmtpPort { get; init; } = 587;

    /// <summary>
    /// API key used as the SMTP password. It needs the <c>emails</c> scope. Defaults to the
    /// factory's API key, or the <c>crosscloud-bird:apiKey</c> stack config secret.
    /// </summary>
    public Input<string>? SmtpApiKey { get; init; }
}
