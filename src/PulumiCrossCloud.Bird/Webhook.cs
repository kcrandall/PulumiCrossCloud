using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// A Bird webhook endpoint (<c>crosscloud-bird:index:Webhook</c>) that receives the listed event
/// types as signed HTTPS POSTs, for example <c>email.received</c> for inbound mail or
/// <c>email.delivered</c> and <c>email.bounced</c> for delivery tracking. Endpoints are workspace
/// wide: they receive the events of every domain in the workspace. Everything updates in place.
/// </summary>
public class Webhook : CustomResource
{
    public const string TypeToken = "crosscloud-bird:index:Webhook";

    [Output("url")]
    public Output<string> Url { get; private set; } = null!;

    [Output("events")]
    public Output<ImmutableArray<string>> Events { get; private set; } = null!;

    [Output("description")]
    public Output<string?> Description { get; private set; } = null!;

    [Output("paused")]
    public Output<bool> Paused { get; private set; } = null!;

    /// <summary>
    /// active, degraded (deliveries failing) or paused.
    /// </summary>
    [Output("status")]
    public Output<string> Status { get; private set; } = null!;

    /// <summary>
    /// The signing secret (<c>whsec_…</c>) for verifying deliveries. Bird only returns it when the
    /// endpoint is created; for an imported endpoint it is empty.
    /// </summary>
    [Output("secret")]
    public Output<string?> Secret { get; private set; } = null!;

    public Webhook(string name, WebhookArgs args, CustomResourceOptions? options = null)
        : base(
            TypeToken,
            name,
            args,
            CustomResourceOptions.Merge(
                CrossCloudPlugins.WithDefaults(options),
                new CustomResourceOptions { AdditionalSecretOutputs = ["secret"] }
            )
        ) { }
}

public sealed class WebhookArgs : ResourceArgs
{
    /// <summary>
    /// Publicly reachable HTTPS URL that receives the events.
    /// </summary>
    [Input("url", required: true)]
    public Input<string> Url { get; set; } = null!;

    private InputList<string>? _events;

    /// <summary>
    /// Event types from Bird's catalog, such as <c>email.received</c>.
    /// </summary>
    [Input("events", required: true)]
    public InputList<string> Events
    {
        get => _events ??= [];
        set => _events = value;
    }

    [Input("description")]
    public Input<string>? Description { get; set; }

    /// <summary>
    /// Stop deliveries without deleting the endpoint.
    /// </summary>
    [Input("paused")]
    public Input<bool>? Paused { get; set; }
}
