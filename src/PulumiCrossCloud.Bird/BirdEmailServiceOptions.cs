using Pulumi;

namespace PulumiCrossCloud.Bird;

public sealed class BirdEmailServiceOptions
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
    /// SMTP submission port: 587 or 2525 (STARTTLS) or 465 (implicit TLS).
    /// </summary>
    public int SmtpPort { get; init; } = 587;

    /// <summary>
    /// API key used as the SMTP password. It needs the <c>emails</c> scope. Defaults to the
    /// factory's API key, or the <c>bird:apiKey</c> stack config secret.
    /// </summary>
    public Input<string>? SmtpApiKey { get; init; }
}
