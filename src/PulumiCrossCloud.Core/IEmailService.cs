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
}
