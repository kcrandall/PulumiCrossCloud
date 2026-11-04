using System.Net;
using System.Text.Json.Nodes;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.PostHog;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class PostHogResourceProviderTests
{
    private const string Organization = OrganizationResource.Type;

    private readonly FakeApiHandler _handler = new();

    [Fact]
    public async Task CreateAdoptsAnExistingOrganizationWithTheSameName()
    {
        var provider = await Configured();
        _handler
            .Respond(
                HttpStatusCode.OK,
                Page(
                    Org("org_0", "Other"),
                    next: "https://eu.posthog.example/api/organizations/?limit=100&offset=100"
                )
            )
            .Respond(HttpStatusCode.OK, Page(Org("org_1", "Acme")));

        var response = await provider.Create(
            new CreateRequest(
                Props.Urn(Organization),
                Props.Of(("name", "Acme")),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Equal("org_1", response.Id);
        Assert.True(PropertyValues.GetBool(ProviderTesting.Map(response.Outputs), "adopted"));
        Assert.Equal("Bearer phx_key", _handler.Requests[0].Authorization);
        Assert.Equal(
            "https://eu.posthog.example/api/organizations/?limit=100&offset=100",
            _handler.Requests[1].Uri.ToString()
        );
        Assert.All(_handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task CreateMakesANewOrganizationWhenNoneMatches()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.OK, Page(Org("org_0", "Other")))
            .Respond(HttpStatusCode.Created, Org("org_2", "Acme"));

        var response = await provider.Create(
            new CreateRequest(
                Props.Urn(Organization),
                Props.Of(("name", "Acme")),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Equal("org_2", response.Id);
        Assert.False(PropertyValues.GetBool(ProviderTesting.Map(response.Outputs), "adopted"));
        Assert.Equal("""{"name":"Acme"}""", _handler.Requests[1].Body!.ToJsonString());
    }

    [Fact]
    public async Task OrganizationCreationIsNotRepeatedAfterAConnectionReset()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Page(Org("org_0", "Other"))).FailConnection();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.Create(
                new CreateRequest(
                    Props.Urn(Organization),
                    Props.Of(("name", "Acme")),
                    TimeSpan.Zero,
                    false
                ),
                default
            )
        );

        // The POST may have created the organization, so it is reported rather than sent twice.
        Assert.Equal(2, _handler.Requests.Count);
        Assert.Contains("PostHog API POST /api/organizations/ could not be reached", error.Message);
    }

    [Fact]
    public async Task RenamesUpdateInPlace()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Org("org_1", "Acme Inc"));
        var olds = Props.Of(("name", "Acme"));
        var news = Props.Of(("name", "Acme Inc"));

        var diff = await provider.Diff(
            new DiffRequest(Props.Urn(Organization), "org_1", olds, news, [], olds),
            default
        );
        await provider.Update(
            new UpdateRequest(
                Props.Urn(Organization),
                "org_1",
                olds,
                news,
                TimeSpan.Zero,
                [],
                false,
                olds
            ),
            default
        );

        Assert.Empty(diff.Replaces!);
        var patch = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Equal("/api/organizations/org_1/", patch.Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task DeleteOnlyRemovesTheOrganizationWhenAskedTo(
        bool deleteOnDestroy,
        int requests
    )
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.NoContent);
        var inputs = Props.Of(("name", "Acme"), ("deleteOnDestroy", deleteOnDestroy));

        await provider.Delete(
            new DeleteRequest(Props.Urn(Organization), "org_1", inputs, TimeSpan.Zero, inputs),
            default
        );

        Assert.Equal(requests, _handler.Requests.Count);
    }

    [Fact]
    public async Task ChangingHostReplacesOrganizations()
    {
        var provider = new PostHogResourceProvider(_handler, _ => null);

        var sameHost = await provider.DiffConfig(
            new DiffRequest(
                Props.Urn("pulumi:providers:crosscloud-posthog"),
                "p",
                Props.Of(),
                Props.Of(("host", "https://us.posthog.com/")),
                [],
                Props.Of()
            ),
            default
        );
        var otherHost = await provider.DiffConfig(
            new DiffRequest(
                Props.Urn("pulumi:providers:crosscloud-posthog"),
                "p",
                Props.Of(),
                Props.Of(("host", "https://eu.posthog.com")),
                [],
                Props.Of()
            ),
            default
        );

        Assert.Empty(sameHost.Replaces!);
        Assert.Equal(["host"], otherHost.Replaces);
    }

    private async Task<PostHogResourceProvider> Configured()
    {
        var provider = new PostHogResourceProvider(
            _handler,
            _ => null,
            (_, _) => Task.CompletedTask
        );
        await provider.Configure(
            ProviderTesting.Configure(
                ("apiKey", "phx_key"),
                ("host", "https://eu.posthog.example")
            ),
            default
        );
        return provider;
    }

    private static JsonObject Org(string id, string name) =>
        new()
        {
            ["id"] = id,
            ["name"] = name,
            ["slug"] = name.ToLowerInvariant(),
        };

    private static JsonObject Page(JsonObject organization, string? next = null) =>
        new() { ["results"] = new JsonArray(organization), ["next"] = next };
}
