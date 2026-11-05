using System.Net;
using System.Text.Json.Nodes;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Cloudflare;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class CloudflareResourceProviderTests
{
    private const string Token = AccountTokenResource.Type;
    private const string Resource = "com.cloudflare.edge.r2.bucket.acct_default_assets";

    private readonly FakeApiHandler _handler = new();

    [Fact]
    public async Task CreateReturnsTheTokenValueAsASecret()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Envelope(TokenJson(value: "bearer-1")));

        var response = await provider.Create(
            new CreateRequest(Props.Urn(Token), Inputs(), TimeSpan.Zero, false),
            default
        );

        Assert.Equal("tok_1", response.Id);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/acct/tokens",
            request.Uri.ToString()
        );
        Assert.Equal("Bearer primary", request.Authorization);
        Assert.Equal(
            $$"""{"name":"assets","policies":[{"effect":"allow","resources":{"{{Resource}}":"*"},"permission_groups":[{"id":"pg_rw"}]}]}""",
            request.Body!.ToJsonString()
        );
        var value = ProviderTesting.Map(response.Outputs)["value"];
        Assert.True(value.IsSecret);
        Assert.True(value.TryGetString(out var text) && text == "bearer-1");
    }

    [Fact]
    public async Task UpdateChangesThePolicyInPlaceAndKeepsTheValue()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Envelope(TokenJson(name: "renamed")));
        var olds = Inputs().SetItem("value", new PropertyValue("bearer-1").WithSecret(true));

        var response = await provider.Update(
            new UpdateRequest(
                Props.Urn(Token),
                "tok_1",
                olds,
                Inputs(name: "renamed", groups: ["pg_rw", "pg_read"]),
                TimeSpan.Zero,
                [],
                false,
                Inputs()
            ),
            default
        );

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith("/accounts/acct/tokens/tok_1", request.Uri.ToString());
        Assert.Equal("renamed", request.Body!["name"]!.GetValue<string>());
        Assert.Equal(2, request.Body!["policies"]![0]!["permission_groups"]!.AsArray().Count);
        var value = ProviderTesting.Map(response.Outputs)["value"];
        Assert.True(value.IsSecret && value.TryGetString(out var text) && text == "bearer-1");
    }

    [Fact]
    public async Task DiffReplacesOnlyForAnotherAccount()
    {
        var provider = await Configured();

        var rename = await provider.Diff(DiffOf(Inputs(), Inputs(name: "renamed")), default);
        var move = await provider.Diff(DiffOf(Inputs(), Inputs(account: "other")), default);

        Assert.Equal(["name"], rename.Diffs);
        Assert.Empty(rename.Replaces!);
        Assert.Equal(["accountId"], move.Replaces);
    }

    [Fact]
    public async Task ReadReportsPolicyDriftAndKeepsTheValue()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Envelope(TokenJson(groups: ["pg_other"])));
        var outputs = Inputs().SetItem("value", new PropertyValue("bearer-1").WithSecret(true));

        var response = await provider.Read(
            new ReadRequest(Props.Urn(Token), "tok_1", outputs, Inputs()),
            default
        );

        var inputs = ProviderTesting.Map(response.Inputs);
        Assert.True(inputs["permissionGroupIds"].TryGetArray(out var groups));
        Assert.Equal("pg_other", groups.Single().TryGetString(out var id) ? id : null);
        Assert.True(ProviderTesting.Map(response.Outputs)["value"].IsSecret);
    }

    [Fact]
    public async Task ReadReportsARevokedTokenAsGone()
    {
        var provider = await Configured();
        _handler.Respond(
            HttpStatusCode.NotFound,
            new JsonObject
            {
                ["success"] = false,
                ["errors"] = new JsonArray(
                    new JsonObject { ["code"] = 1003, ["message"] = "Not found" }
                ),
            }
        );

        var response = await provider.Read(
            new ReadRequest(Props.Urn(Token), "tok_1", Inputs(), Inputs()),
            default
        );

        Assert.Null(response.Id);
    }

    [Fact]
    public async Task DeleteRevokesTheToken()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Envelope(new JsonObject { ["id"] = "tok_1" }));

        await provider.Delete(
            new DeleteRequest(Props.Urn(Token), "tok_1", Inputs(), TimeSpan.Zero, Inputs()),
            default
        );

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.EndsWith("/accounts/acct/tokens/tok_1", request.Uri.ToString());
    }

    [Fact]
    public async Task ErrorsIncludeCloudflaresCodeAndMessage()
    {
        var provider = await Configured();
        _handler.Respond(
            HttpStatusCode.Forbidden,
            new JsonObject
            {
                ["success"] = false,
                ["errors"] = new JsonArray(
                    new JsonObject { ["code"] = 9109, ["message"] = "Unauthorized to access requested resource" }
                ),
            }
        );

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            provider.Create(new CreateRequest(Props.Urn(Token), Inputs(), TimeSpan.Zero, false), default)
        );

        Assert.Equal("9109", error.Code);
        Assert.Contains("Unauthorized to access requested resource", error.Message);
    }

    [Fact]
    public async Task CheckRequiresPermissionsAndResources()
    {
        var provider = await Configured();

        var response = await provider.Check(
            new CheckRequest(
                Props.Urn(Token),
                Props.Of(),
                Props.Of(("accountId", "acct"), ("name", "assets"), ("permissionGroupIds", Array.Empty<string>())),
                []
            ),
            default
        );

        Assert.Equal(
            ["permissionGroupIds", "resources"],
            response.Failures!.Select(f => f.Property).Order()
        );
    }

    private static DiffRequest DiffOf(
        System.Collections.Immutable.ImmutableDictionary<string, PropertyValue> olds,
        System.Collections.Immutable.ImmutableDictionary<string, PropertyValue> news
    ) => new(Props.Urn(Token), "tok_1", olds, news, [], olds);

    private static System.Collections.Immutable.ImmutableDictionary<string, PropertyValue> Inputs(
        string account = "acct",
        string name = "assets",
        string[]? groups = null
    ) =>
        Props.Of(
            ("accountId", account),
            ("name", name),
            ("permissionGroupIds", groups ?? ["pg_rw"]),
            ("resources", new Dictionary<string, string> { [Resource] = "*" })
        );

    private async Task<CloudflareResourceProvider> Configured()
    {
        var provider = new CloudflareResourceProvider(_handler, _ => null, (_, _) => Task.CompletedTask);
        await provider.Configure(ProviderTesting.Configure(("apiToken", "primary")), default);
        return provider;
    }

    private static JsonObject TokenJson(
        string name = "assets",
        string? value = null,
        string[]? groups = null
    ) =>
        new()
        {
            ["id"] = "tok_1",
            ["name"] = name,
            ["status"] = "active",
            ["value"] = value,
            ["policies"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "pol_1",
                    ["effect"] = "allow",
                    ["resources"] = new JsonObject { [Resource] = "*" },
                    ["permission_groups"] = new JsonArray(
                        [.. (groups ?? ["pg_rw"]).Select(id => (JsonNode)new JsonObject { ["id"] = id, ["name"] = id })]
                    ),
                }
            ),
        };

    private static JsonObject Envelope(JsonObject result) =>
        new()
        {
            ["success"] = true,
            ["errors"] = new JsonArray(),
            ["result"] = result,
        };
}
