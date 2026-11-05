using System.Collections.Immutable;
using System.Net;
using System.Text.Json.Nodes;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Bird;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class BirdWebhookAndSuppressionTests
{
    private const string Webhook = WebhookResource.Type;
    private const string Suppression = SuppressionResource.Type;

    private readonly FakeApiHandler _handler = new();

    [Fact]
    public async Task WebhookCreationKeepsTheSigningSecretAsASecret()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.Created, WebhookJson(secret: "whsec_abc"));

        var response = await provider.Create(
            new CreateRequest(Props.Urn(Webhook), WebhookInputs(), TimeSpan.Zero, false),
            default
        );

        Assert.Equal("whk_1", response.Id);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://us1.platform.bird.com/v1/webhooks", request.Uri.ToString());
        Assert.NotNull(request.IdempotencyKey);
        Assert.Equal(
            """{"url":"https://app.example.com/inbound","events":["email.received"],"description":"Inbound"}""",
            request.Body!.ToJsonString()
        );
        var secret = ProviderTesting.Map(response.Outputs)["secret"];
        Assert.True(secret.IsSecret && secret.TryGetString(out var value) && value == "whsec_abc");
    }

    [Fact]
    public async Task WebhookUpdatesInPlaceAndKeepsTheSecret()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, WebhookJson(status: "paused"));
        var olds = WebhookInputs().SetItem("secret", new PropertyValue("whsec_abc").WithSecret(true));

        var diff = await provider.Diff(
            new DiffRequest(Props.Urn(Webhook), "whk_1", olds, WebhookInputs().SetItem("paused", new PropertyValue(true)), [], WebhookInputs()),
            default
        );
        var response = await provider.Update(
            new UpdateRequest(
                Props.Urn(Webhook),
                "whk_1",
                olds,
                WebhookInputs().SetItem("paused", new PropertyValue(true)),
                TimeSpan.Zero,
                [],
                false,
                WebhookInputs()
            ),
            default
        );

        Assert.Empty(diff.Replaces!);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("paused", request.Body!["status"]!.GetValue<string>());
        var outputs = ProviderTesting.Map(response.Outputs);
        Assert.True(outputs["secret"].TryGetString(out var secret) && secret == "whsec_abc");
        Assert.True(PropertyValues.GetBool(outputs, "paused"));
    }

    [Fact]
    public async Task WebhookReadReportsEventDrift()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, WebhookJson(events: ["email.received", "email.bounced"]));

        var response = await provider.Read(
            new ReadRequest(Props.Urn(Webhook), "whk_1", Props.Of(), WebhookInputs()),
            default
        );

        Assert.True(ProviderTesting.Map(response.Inputs)["events"].TryGetArray(out var events));
        Assert.Equal(2, events.Length);
    }

    [Fact]
    public async Task WebhookRequiresHttps()
    {
        var provider = await Configured();

        var response = await provider.Check(
            new CheckRequest(
                Props.Urn(Webhook),
                Props.Of(),
                Props.Of(("url", "http://app.example.com"), ("events", new[] { "email.received" })),
                []
            ),
            default
        );

        Assert.Equal("url", Assert.Single(response.Failures!).Property);
    }

    [Fact]
    public async Task SuppressionsAreCreatedAndRemovedById()
    {
        var provider = await Configured();
        _handler
            .Respond(
                HttpStatusCode.Created,
                new JsonObject { ["id"] = "sup_1", ["email"] = "blocked@example.com", ["reason"] = "manual" }
            )
            .Respond(HttpStatusCode.NoContent);
        var inputs = Props.Of(("email", "blocked@example.com"));

        var created = await provider.Create(
            new CreateRequest(Props.Urn(Suppression), inputs, TimeSpan.Zero, false),
            default
        );
        await provider.Delete(
            new DeleteRequest(Props.Urn(Suppression), "sup_1", inputs, TimeSpan.Zero, inputs),
            default
        );

        Assert.Equal("sup_1", created.Id);
        Assert.Equal("https://us1.platform.bird.com/v1/email/suppressions", _handler.Requests[0].Uri.ToString());
        Assert.Equal(HttpMethod.Delete, _handler.Requests[1].Method);
        Assert.Equal(
            "https://us1.platform.bird.com/v1/email/suppressions/sup_1",
            _handler.Requests[1].Uri.ToString()
        );
    }

    [Fact]
    public async Task ChangingTheSuppressedAddressReplacesIt()
    {
        var provider = await Configured();
        var olds = Props.Of(("email", "a@example.com"));

        var diff = await provider.Diff(
            new DiffRequest(Props.Urn(Suppression), "sup_1", olds, Props.Of(("email", "b@example.com")), [], olds),
            default
        );

        Assert.Equal(["email"], diff.Replaces);
    }

    private static ImmutableDictionary<string, PropertyValue> WebhookInputs() =>
        Props.Of(
            ("url", "https://app.example.com/inbound"),
            ("events", new[] { "email.received" }),
            ("description", "Inbound")
        );

    private static JsonObject WebhookJson(
        string? secret = null,
        string status = "active",
        string[]? events = null
    ) =>
        new()
        {
            ["id"] = "whk_1",
            ["url"] = "https://app.example.com/inbound",
            ["description"] = "Inbound",
            ["events"] = new JsonArray([.. (events ?? ["email.received"]).Select(e => (JsonNode)e)]),
            ["status"] = status,
            ["secret"] = secret,
        };

    private async Task<BirdResourceProvider> Configured()
    {
        var provider = new BirdResourceProvider(_handler, _ => null, (_, _) => Task.CompletedTask);
        await provider.Configure(
            ProviderTesting.Configure(("apiKey", new PropertyValue("bk_us1_test").WithSecret(true))),
            default
        );
        return provider;
    }
}
