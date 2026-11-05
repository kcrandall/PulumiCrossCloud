using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Mailgun;

public sealed record MailgunEmailServiceOptions
{
    /// <summary>
    /// Rewrite links to count clicks. Null leaves the domain's setting unmanaged (Mailgun's
    /// default is off). Changes apply in place.
    /// </summary>
    public bool? ClickTracking { get; init; }

    /// <summary>
    /// Add a pixel to count opens. Null leaves the domain's setting unmanaged (Mailgun's default
    /// is off). Changes apply in place.
    /// </summary>
    public bool? OpenTracking { get; init; }

    /// <summary>
    /// Local part of a dedicated SMTP login (for example <c>app</c> for
    /// <c>app@mail.example.com</c>) with its own generated password. The service then hands out
    /// that login instead of the domain's <c>postmaster</c> login, and it can be revoked on its
    /// own. Null keeps <c>postmaster</c>. Switching an existing stack issues new credentials;
    /// the postmaster login keeps working.
    /// </summary>
    public string? SmtpLogin { get; init; }

    /// <summary>
    /// Forward mail sent to any address at the domain to a URL through a Mailgun route.
    /// Mailgun posts each parsed message as a multipart form, signed with the account's HTTP
    /// webhook signing key. The domain's MX records are already among its DNS records.
    /// </summary>
    public InboundMailOptions? InboundMail { get; init; }
}
