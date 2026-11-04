using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Pulumi;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;

namespace PulumiCrossCloud.Plugin.Tests;

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Authorization,
    string? IdempotencyKey,
    JsonNode? Body
);

/// <summary>
/// Replays queued Bird API responses and records the requests that consumed them.
/// </summary>
internal sealed class FakeApiHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>
    /// Fails the next request at the transport level, like a connection reset.
    /// </summary>
    public FakeApiHandler FailConnection()
    {
        _responses.Enqueue(() => throw new HttpRequestException("Connection reset by peer"));
        return this;
    }

    public FakeApiHandler Respond(
        HttpStatusCode status,
        JsonNode? body = null,
        TimeSpan? retryAfter = null
    )
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
                response.Content = new StringContent(
                    body.ToJsonString(),
                    Encoding.UTF8,
                    "application/json"
                );
            if (retryAfter is not null)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    retryAfter.Value
                );
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? null
            : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Add(
            new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys)
                    ? keys.Single()
                    : null,
                body
            )
        );
        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"Unexpected request {request.Method} {request.RequestUri}"
            );
        return _responses.Dequeue()();
    }
}

internal static class BirdFixtures
{
    public static JsonObject Domain(
        string id = "dom_1",
        string domain = "mail.example.com",
        string status = "pending",
        string sendingStatus = "pending",
        string? trackingDomain = "links.mail.example.com",
        bool clickTracking = false,
        string? receivingState = null,
        string inboundStatus = "not_configured",
        string? defaultIpPoolId = null,
        params JsonObject[] records
    ) =>
        new()
        {
            ["id"] = id,
            ["workspace_id"] = "ws_1",
            ["default_ip_pool_id"] = defaultIpPoolId,
            ["receiving"] = receivingState is null
                ? null
                : new JsonObject { ["state"] = receivingState, ["managed_elsewhere"] = false },
            ["domain"] = domain,
            ["status"] = status,
            ["settings"] = new JsonObject
            {
                ["click_tracking"] = clickTracking,
                ["open_tracking"] = false,
            },
            ["dkim"] = new JsonObject { ["mode"] = "txt", ["selector"] = "bird1" },
            ["capabilities"] = new JsonObject
            {
                ["sending"] = new JsonObject { ["status"] = sendingStatus },
                ["return_path"] = new JsonObject
                {
                    ["status"] = "pending",
                    ["domain"] = $"send.{domain}",
                },
                ["tracking"] = trackingDomain is null
                    ? new JsonObject { ["status"] = "not_configured", ["domain"] = null }
                    : new JsonObject { ["status"] = "pending", ["domain"] = trackingDomain },
                ["inbound"] = new JsonObject { ["status"] = inboundStatus },
            },
            ["dns_records"] = new JsonArray(
                records.Length > 0
                    ? [.. records]
                    : [Record("dkim", "TXT", $"bird1._domainkey.{domain}", "v=DKIM1; k=rsa; p=abc")]
            ),
            ["verified_at"] = null,
        };

    public static JsonObject Record(
        string purpose,
        string type,
        string host,
        string value,
        string state = "active",
        string status = "pending",
        bool optional = false,
        string? error = null
    ) =>
        new()
        {
            ["type"] = type,
            ["name"] = host,
            ["host"] = host,
            ["value"] = value,
            ["purpose"] = purpose,
            ["state"] = state,
            ["optional"] = optional,
            ["status"] = status,
            ["error"] = error,
        };

    public static JsonObject Error(string code, string message) =>
        new()
        {
            ["error"] = new JsonObject
            {
                ["type"] = "conflict_error",
                ["code"] = code,
                ["message"] = message,
                ["request_id"] = "req_1",
            },
        };
}

internal static class Props
{
    public static ImmutableDictionary<string, PropertyValue> Of(
        params (string Key, object? Value)[] values
    ) => values.ToImmutableDictionary(pair => pair.Key, pair => Value(pair.Value));

    public static PropertyValue Value(object? value) =>
        value switch
        {
            null => PropertyValue.Null,
            PropertyValue property => property,
            string text => new PropertyValue(text),
            bool flag => new PropertyValue(flag),
            string[] items => new PropertyValue([.. items.Select(item => new PropertyValue(item))]),
            _ => throw new ArgumentException($"Unsupported test value {value}"),
        };

    public static Urn Urn(string type, string name = "test") =>
        new($"urn:pulumi:dev::proj::{type}::{name}");
}

internal static class ProviderTesting
{
    public static ConfigureRequest Configure(params (string Key, object? Value)[] values) =>
        new(
            ImmutableDictionary<string, string>.Empty,
            Props.Of(values),
            acceptSecrets: true,
            acceptResources: false,
            sendsOldInputs: true,
            sendsOldInputsToDelete: true
        );

    public static Func<TimeSpan, CancellationToken, Task> RecordDelays(List<TimeSpan> delays) =>
        (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };

    public static ImmutableDictionary<string, PropertyValue> Map(
        IDictionary<string, PropertyValue>? values
    ) => values!.ToImmutableDictionary();
}
