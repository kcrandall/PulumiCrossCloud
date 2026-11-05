using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Bird;

/// <summary>
/// <c>crosscloud-bird:index:Webhook</c>: a workspace webhook endpoint that receives the listed
/// event types, such as <c>email.received</c> for inbound mail. The URL, events, description and
/// paused state update in place. Bird returns the signing secret only on creation, so it is
/// kept from then on.
/// </summary>
internal sealed class WebhookResource(Func<BirdApiClient> client) : IResourceHandler
{
    public const string Type = "crosscloud-bird:index:Webhook";

    private const string Url = "url";
    private const string Events = "events";
    private const string Description = "description";
    private const string Paused = "paused";
    private const string Secret = "secret";

    private static readonly string[] Inputs = [Url, Events, Description, Paused];
    private static readonly IReadOnlySet<string> RequiredInputs = new HashSet<string>
    {
        Url,
        Events,
    };

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        var url = PropertyValues.GetString(inputs, Url);
        if (
            !PropertyValues.IsComputed(inputs, Url)
            && (
                url is null
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
            )
        )
            failures.Add(new CheckFailure(Url, "url must be an absolute https URL."));

        var events = PropertyValues.Get(inputs, Events);
        if (!events.IsComputed && (!events.TryGetArray(out var items) || items.IsEmpty))
            failures.Add(new CheckFailure(Events, "Subscribe to at least one event type."));

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Task.FromResult(
            InputDiff.Compute(request, Inputs, new HashSet<string>(), RequiredInputs)
        );

    public async Task<CreateResponse> Create(CreateRequest request, CancellationToken ct)
    {
        var inputs = request.Inputs;
        if (request.Preview)
            return new CreateResponse
            {
                Outputs = PreviewOutputs(inputs, PropertyValue.Computed.WithSecret(true)),
            };

        var api = client();
        var webhook = await api.CreateWebhookAsync(Body(inputs, includeStatus: false), ct);
        var secret = webhook.Secret;
        if (PropertyValues.GetBool(inputs, Paused) == true)
            webhook = await api.UpdateWebhookAsync(
                webhook.Id,
                new JsonObject { ["status"] = "paused" },
                ct
            );
        return new CreateResponse { Id = webhook.Id, Outputs = ToOutputs(webhook, secret) };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var webhook = await client().GetWebhookAsync(request.Id, ct);
        if (webhook is null)
            return new ReadResponse();

        var inputs = (request.Inputs.IsEmpty ? PropertyValues.Empty : request.Inputs)
            .SetItem(Url, new PropertyValue(webhook.Url))
            .SetItem(Events, Strings(webhook.Events));
        if (!request.Inputs.IsEmpty && !PropertyValues.Get(request.Inputs, Description).IsNull)
            inputs = inputs.SetItem(Description, PropertyValues.String(webhook.Description));
        if (!request.Inputs.IsEmpty && !PropertyValues.Get(request.Inputs, Paused).IsNull)
            inputs = inputs.SetItem(Paused, new PropertyValue(webhook.Status == "paused"));

        return new ReadResponse
        {
            Id = webhook.Id,
            Inputs = inputs,
            Outputs = ToOutputs(webhook, PropertyValues.GetString(request.Outputs, Secret)),
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        var secret = PropertyValues.GetString(request.OldOutputs, Secret);
        if (request.Preview)
            return new UpdateResponse
            {
                Outputs = PreviewOutputs(inputs, PropertyValues.String(secret).WithSecret(true)),
            };

        var webhook = await client()
            .UpdateWebhookAsync(request.Id, Body(inputs, includeStatus: true), ct);
        return new UpdateResponse { Outputs = ToOutputs(webhook, secret) };
    }

    public async Task Delete(DeleteRequest request, CancellationToken ct) =>
        await client().DeleteWebhookAsync(request.Id, ct);

    private static JsonObject Body(
        ImmutableDictionary<string, PropertyValue> inputs,
        bool includeStatus
    )
    {
        PropertyValues.Get(inputs, Events).TryGetArray(out var events);
        var body = new JsonObject
        {
            ["url"] = PropertyValues.GetString(inputs, Url),
            ["events"] = new JsonArray(
                [.. events.Select(item => (JsonNode?)(item.TryGetString(out var name) ? name : null))]
            ),
        };
        if (PropertyValues.GetString(inputs, Description) is { } description)
            body["description"] = description;
        if (includeStatus && PropertyValues.GetBool(inputs, Paused) is { } paused)
            body["status"] = paused ? "paused" : "active";
        return body;
    }

    private static PropertyValue Strings(IEnumerable<string> values) =>
        new([.. values.Select(value => new PropertyValue(value))]);

    private static ImmutableDictionary<string, PropertyValue> ToOutputs(
        BirdWebhook webhook,
        string? secret
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(Url, new PropertyValue(webhook.Url))
            .Add(Events, Strings(webhook.Events))
            .Add(Description, PropertyValues.String(webhook.Description))
            .Add(Paused, new PropertyValue(webhook.Status == "paused"))
            .Add("status", new PropertyValue(webhook.Status))
            .Add(Secret, PropertyValues.String(secret).WithSecret(true));

    private static ImmutableDictionary<string, PropertyValue> PreviewOutputs(
        ImmutableDictionary<string, PropertyValue> inputs,
        PropertyValue secret
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(Url, PropertyValues.Get(inputs, Url))
            .Add(Events, PropertyValues.Get(inputs, Events))
            .Add(Description, PropertyValues.Get(inputs, Description))
            .Add(Paused, new PropertyValue(PropertyValues.GetBool(inputs, Paused) ?? false))
            .Add("status", PropertyValue.Computed)
            .Add(Secret, secret);
}
