using Pulumi;

namespace PulumiCrossCloud.Core;

/// <summary>
/// Requests inbound mail for an email service's domain: every message sent to any address at
/// the domain is posted to <see cref="WebhookUrl"/>. Use a dedicated subdomain, such as
/// <c>inbound.example.com</c>, because receiving publishes MX records for the domain.
/// </summary>
public sealed class InboundMailOptions
{
    /// <summary>
    /// Publicly reachable HTTPS URL that receives each inbound message.
    /// </summary>
    public required Input<string> WebhookUrl { get; init; }
}

/// <summary>
/// Where an email service delivers inbound mail. The request format depends on the provider:
/// Mailgun forwards each parsed message as a multipart form POST signed with the account's HTTP
/// webhook signing key; Bird posts an <c>email.received</c> event signed with
/// <see cref="SigningSecret"/>, whose <c>inbound_message_id</c> fetches the message.
/// </summary>
public sealed class InboundMailEndpoint(
    Output<string> webhookUrl,
    Output<string?>? signingSecret,
    Resource ready
)
{
    /// <summary>
    /// The URL inbound messages are posted to.
    /// </summary>
    public Output<string> WebhookUrl { get; } = webhookUrl;

    /// <summary>
    /// The secret that signs deliveries to this endpoint, when the provider issues one per
    /// endpoint; null when deliveries are signed with an account-wide key.
    /// </summary>
    public Output<string?>? SigningSecret { get; } = signingSecret;

    /// <summary>
    /// The resource whose completion means mail is routed to the URL.
    /// </summary>
    public Resource Ready { get; } = ready;
}
