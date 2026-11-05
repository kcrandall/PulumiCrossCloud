using Pulumi;

namespace PulumiCrossCloud.Core;

/// <summary>
/// Represents a managed email service (e.g., SES, Mailgun).
/// </summary>
public interface IEmailService : ICloudResource, IPolicyProvider<EmailPermissions>
{
    /// <summary>
    /// The SMTP endpoint for sending emails.
    /// </summary>
    Output<string> SmtpEndpoint { get; }

    /// <summary>
    /// The SMTP port.
    /// </summary>
    Output<int> SmtpPort { get; }

    /// <summary>
    /// The username for SMTP authentication.
    /// </summary>
    Output<string> Username { get; }

    /// <summary>
    /// The password/API key for SMTP authentication.
    /// </summary>
    ISecret Password { get; }

    /// <summary>
    /// The DNS records required to verify and configure the email service (SPF, DKIM, MX, CNAME).
    /// </summary>
    IEnumerable<DnsRecord> DnsRecords { get; }

    /// <summary>
    /// The resource whose completion means the service can send: a domain verification when the
    /// provider supports one, otherwise the domain itself. Make anything that sends mail, such as
    /// an application deployment, depend on it.
    /// </summary>
    Resource Ready => Password.ValueResource;

    /// <summary>
    /// Where inbound mail is delivered; null unless the service was created with
    /// <see cref="InboundMailOptions"/>.
    /// </summary>
    InboundMailEndpoint? InboundMail => null;
}
