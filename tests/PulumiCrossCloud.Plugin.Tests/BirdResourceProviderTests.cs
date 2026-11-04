using System.Collections.Immutable;
using System.Net;
using System.Text.Json.Nodes;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Bird;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class BirdResourceProviderTests
{
    private const string Domain = SendingDomainResource.Type;
    private const string Verification = DomainVerificationResource.Type;
    private const string Receiving = DomainReceivingResource.Type;

    private readonly FakeApiHandler _handler = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly Dictionary<string, string> _environment = [];

    [Fact]
    public async Task CheckConfigRejectsUnknownRegionsAndKeysFromAnotherRegion()
    {
        var provider = Provider();

        var unknown = await provider.CheckConfig(
            ConfigCheck(("apiKey", "bk_us1_x"), ("region", "ap1")),
            default
        );
        var mismatch = await provider.CheckConfig(
            ConfigCheck(("apiKey", "bk_us1_x"), ("region", "eu1")),
            default
        );
        var missingKey = await provider.CheckConfig(ConfigCheck(("region", "us1")), default);

        Assert.Contains(unknown.Failures!, failure => failure.Property == "region");
        Assert.Contains(mismatch.Failures!, failure => failure.Reason.Contains("belongs to 'us1'"));
        Assert.Contains(missingKey.Failures!, failure => failure.Property == "apiKey");
    }

    [Fact]
    public async Task CheckConfigAcceptsAnApiKeyFromTheEnvironment()
    {
        _environment["BIRD_API_KEY"] = "bk_us1_env";

        var response = await Provider().CheckConfig(ConfigCheck(), default);

        Assert.Empty(response.Failures!);
    }

    [Fact]
    public async Task ConfigureUsesTheRegionEncodedInTheApiKey()
    {
        var provider = await Configured("bm_eu1_master");
        _handler.Respond(HttpStatusCode.OK, BirdFixtures.Domain());

        await provider.Read(
            new ReadRequest(Props.Urn(Domain), "dom_1", Props.Of(), Props.Of()),
            default
        );

        Assert.Equal("eu1.platform.bird.com", _handler.Requests.Single().Uri.Host);
    }

    [Fact]
    public async Task DiffConfigOnlyReplacesWhenTheEffectiveRegionChanges()
    {
        var provider = Provider();

        var spelledOut = await provider.DiffConfig(
            Diff(
                "bird",
                Props.Of(("apiKey", "bk_us1_a")),
                Props.Of(("apiKey", "bk_us1_a"), ("region", "us1"))
            ),
            default
        );
        var rotated = await provider.DiffConfig(
            Diff("bird", Props.Of(("apiKey", "bk_us1_a")), Props.Of(("apiKey", "bk_us1_b"))),
            default
        );
        var moved = await provider.DiffConfig(
            Diff("bird", Props.Of(("apiKey", "bk_us1_a")), Props.Of(("apiKey", "bk_eu1_b"))),
            default
        );

        Assert.Empty(spelledOut.Replaces!);
        Assert.Equal(["apiKey"], rotated.Diffs);
        Assert.Empty(rotated.Replaces!);
        Assert.Equal(["region"], moved.Replaces!);
    }

    [Theory]
    [InlineData("Mail.Example.com", "domain")]
    [InlineData("localhost", "domain")]
    [InlineData("mail.example.com", null)]
    public async Task CheckValidatesTheDomain(string domain, string? failedProperty)
    {
        var response = await Provider().Check(Check(Domain, ("domain", domain)), default);

        Assert.Equal(failedProperty, response.Failures!.SingleOrDefault()?.Property);
    }

    [Fact]
    public async Task CheckRejectsTrackingTogglesWithoutATrackingDomain()
    {
        var response = await Provider()
            .Check(
                Check(
                    Domain,
                    ("domain", "mail.example.com"),
                    ("trackingName", ""),
                    ("clickTracking", true)
                ),
                default
            );

        Assert.Equal("clickTracking", Assert.Single(response.Failures!).Property);
    }

    [Fact]
    public async Task DiffReplacesOnDomainChangesAndUpdatesSettingsInPlace()
    {
        var provider = Provider();
        var olds = Props.Of(("domain", "mail.example.com"), ("trackingName", "links"));

        var renamed = await provider.Diff(
            Diff(Domain, olds, Props.Of(("domain", "mail.example.org"), ("trackingName", "links"))),
            default
        );
        var tracking = await provider.Diff(
            Diff(Domain, olds, Props.Of(("domain", "mail.example.com"), ("trackingName", "t"))),
            default
        );
        var unmanaged = await provider.Diff(
            Diff(Domain, olds, Props.Of(("domain", "mail.example.com"))),
            default
        );

        Assert.Equal(["domain"], renamed.Replaces!);
        Assert.Equal(PropertyDiffKind.UpdateReplace, renamed.DetailedDiff!["domain"].Kind);
        Assert.Equal(["trackingName"], tracking.Diffs);
        Assert.Empty(tracking.Replaces!);
        Assert.False(unmanaged.Changes);
    }

    [Fact]
    public async Task PreviewCreateMakesNoApiCallsAndMarksBirdValuesUnknown()
    {
        var provider = await Configured();

        var response = await provider.Create(
            new CreateRequest(
                Props.Urn(Domain),
                Props.Of(("domain", "mail.example.com")),
                TimeSpan.Zero,
                preview: true
            ),
            default
        );

        Assert.Empty(_handler.Requests);
        Assert.Equal(
            "mail.example.com",
            PropertyValues.GetString(Outputs(response.Outputs), "domain")
        );
        Assert.True(response.Outputs!["dnsRecords"].IsComputed);
        Assert.True(response.Outputs["returnPathName"].IsComputed);
    }

    [Fact]
    public async Task CreateRegistersTheDomainAndRemovesDefaultTrackingWhenDisabled()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.Created, BirdFixtures.Domain())
            .Respond(HttpStatusCode.OK, BirdFixtures.Domain(trackingDomain: null));

        var response = await provider.Create(
            new CreateRequest(
                Props.Urn(Domain),
                Props.Of(
                    ("domain", "mail.example.com"),
                    ("returnPathName", "send"),
                    ("trackingName", "")
                ),
                TimeSpan.Zero,
                preview: false
            ),
            default
        );

        Assert.Equal("dom_1", response.Id);
        var create = _handler.Requests[0];
        Assert.Equal("send", create.Body!["return_path"]!["name"]!.GetValue<string>());
        Assert.Null(create.Body["tracking"]);
        var patch = _handler.Requests[1];
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.True(patch.Body!.AsObject().ContainsKey("tracking"));
        Assert.Null(patch.Body["tracking"]);

        var outputs = Outputs(response.Outputs);
        Assert.Equal("", PropertyValues.GetString(outputs, "trackingName"));
        Assert.Equal("send", PropertyValues.GetString(outputs, "returnPathName"));
        Assert.Equal("us1", PropertyValues.GetString(outputs, "region"));
        Assert.True(outputs["dnsRecords"].TryGetArray(out var records));
        Assert.True(records.Single().TryGetMap(out var record));
        Assert.Equal("bird1._domainkey.mail.example.com", PropertyValues.GetString(record, "host"));
    }

    [Fact]
    public async Task CreateExplainsHowToAdoptAnExistingDomain()
    {
        var provider = await Configured();
        _handler.Respond(
            HttpStatusCode.Conflict,
            BirdFixtures.Error("E05001", "Domain already registered.")
        );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.Create(
                new CreateRequest(
                    Props.Urn(Domain),
                    Props.Of(("domain", "mail.example.com")),
                    TimeSpan.Zero,
                    false
                ),
                default
            )
        );

        Assert.Contains("pulumi import crosscloud-bird:index:SendingDomain", error.Message);
    }

    [Fact]
    public async Task ReadReportsDeletedDomainsAndImportsLiveSettings()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.NotFound, BirdFixtures.Error("E04040", "Not found."))
            .Respond(HttpStatusCode.OK, BirdFixtures.Domain(clickTracking: true));

        var gone = await provider.Read(
            new ReadRequest(Props.Urn(Domain), "dom_1", Props.Of(), Props.Of()),
            default
        );
        var imported = await provider.Read(
            new ReadRequest(Props.Urn(Domain), "dom_1", Props.Of(), Props.Of()),
            default
        );

        Assert.True(string.IsNullOrEmpty(gone.Id));
        Assert.Equal("dom_1", imported.Id);
        Assert.Equal(
            "mail.example.com",
            PropertyValues.GetString(Outputs(imported.Inputs), "domain")
        );
        Assert.Equal("links", PropertyValues.GetString(Outputs(imported.Inputs), "trackingName"));
        Assert.True(PropertyValues.GetBool(Outputs(imported.Inputs), "clickTracking"));
    }

    [Fact]
    public async Task RefreshOnlyReportsDriftForManagedInputs()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, BirdFixtures.Domain(clickTracking: true));

        var refreshed = await provider.Read(
            new ReadRequest(
                Props.Urn(Domain),
                "dom_1",
                Props.Of(),
                Props.Of(("domain", "mail.example.com"), ("returnPathName", "bounce"))
            ),
            default
        );

        var inputs = Outputs(refreshed.Inputs);
        Assert.Equal("send", PropertyValues.GetString(inputs, "returnPathName"));
        Assert.False(inputs.ContainsKey("clickTracking"));
    }

    [Fact]
    public async Task UpdatePatchesOnlyChangedSettings()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, BirdFixtures.Domain(clickTracking: true));
        var olds = Props.Of(
            ("domain", "mail.example.com"),
            ("returnPathName", "send"),
            ("clickTracking", false)
        );
        var news = Props.Of(
            ("domain", "mail.example.com"),
            ("returnPathName", "send"),
            ("clickTracking", true)
        );

        await provider.Update(
            new UpdateRequest(
                Props.Urn(Domain),
                "dom_1",
                olds,
                news,
                TimeSpan.Zero,
                [],
                false,
                olds
            ),
            default
        );

        var patch = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Equal("""{"settings":{"click_tracking":true}}""", patch.Body!.ToJsonString());
    }

    [Fact]
    public async Task VerificationPollsWithBackoffUntilTheDomainCanSend()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.OK, BirdFixtures.Domain())
            .Respond(HttpStatusCode.OK, BirdFixtures.Domain(status: "verified"))
            .Respond(
                HttpStatusCode.OK,
                BirdFixtures.Domain(status: "verified", sendingStatus: "verified")
            );

        var response = await provider.Create(
            new CreateRequest(
                Props.Urn(Verification),
                Props.Of(("domainId", "dom_1")),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Equal("dom_1/verification", response.Id);
        Assert.Equal(
            3,
            _handler.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("/verify"))
        );
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15)], _delays);
        Assert.Equal(
            "verified",
            PropertyValues.GetString(Outputs(response.Outputs), "sendingStatus")
        );
    }

    [Fact]
    public async Task VerificationTimesOutWithTheRecordsThatStillNeedAttention()
    {
        var provider = await Configured();
        for (var i = 0; i < 3; i++)
            _handler.Respond(
                HttpStatusCode.OK,
                BirdFixtures.Domain(
                    records:
                    [
                        BirdFixtures.Record(
                            "dkim",
                            "TXT",
                            "bird1._domainkey.mail.example.com",
                            "v=DKIM1",
                            status: "verified"
                        ),
                        BirdFixtures.Record(
                            "return_path",
                            "CNAME",
                            "send.mail.example.com",
                            "rp.bird.com",
                            error: "found other.example"
                        ),
                        BirdFixtures.Record(
                            "inbound_mx",
                            "MX",
                            "mail.example.com",
                            "10 mx.bird.com",
                            optional: true
                        ),
                    ]
                )
            );

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            provider.Create(
                new CreateRequest(
                    Props.Urn(Verification),
                    Props.Of(("domainId", "dom_1")),
                    TimeSpan.FromSeconds(30),
                    false
                ),
                default
            )
        );

        Assert.Contains(
            "return_path CNAME send.mail.example.com: pending (found other.example)",
            error.Message
        );
        Assert.DoesNotContain("bird1._domainkey", error.Message);
        Assert.DoesNotContain("inbound_mx", error.Message);
    }

    [Fact]
    public async Task VerificationCanTriggerAChecksWithoutWaiting()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, BirdFixtures.Domain());

        await provider.Create(
            new CreateRequest(
                Props.Urn(Verification),
                Props.Of(("domainId", "dom_1"), ("waitForSending", false)),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Single(_handler.Requests);
        Assert.Empty(_delays);
    }

    [Fact]
    public async Task VerificationReplacesWhenTriggersChange()
    {
        var olds = Props.Of(("domainId", "dom_1"), ("triggers", new[] { "a" }));
        var news = Props.Of(("domainId", "dom_1"), ("triggers", new[] { "b" }));

        var response = await Provider().Diff(Diff(Verification, olds, news), default);

        Assert.Equal(["triggers"], response.Replaces!);
    }

    [Fact]
    public async Task CreateSetsTheDefaultIpPoolAfterRegistering()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.Created, BirdFixtures.Domain())
            .Respond(HttpStatusCode.OK, BirdFixtures.Domain(defaultIpPoolId: "ipp_1"));

        var response = await provider.Create(
            new CreateRequest(
                Props.Urn(Domain),
                Props.Of(("domain", "mail.example.com"), ("defaultIpPoolId", "ipp_1")),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Equal(
            """{"default_ip_pool_id":"ipp_1"}""",
            _handler.Requests[1].Body!.ToJsonString()
        );
        Assert.Equal(
            "ipp_1",
            PropertyValues.GetString(ProviderTesting.Map(response.Outputs), "defaultIpPoolId")
        );
    }

    [Fact]
    public async Task ClearingTheIpPoolSendsNull()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, BirdFixtures.Domain());
        var olds = Props.Of(("domain", "mail.example.com"), ("defaultIpPoolId", "ipp_1"));
        var news = Props.Of(("domain", "mail.example.com"), ("defaultIpPoolId", ""));

        await provider.Update(
            new UpdateRequest(
                Props.Urn(Domain),
                "dom_1",
                olds,
                news,
                TimeSpan.Zero,
                [],
                false,
                olds
            ),
            default
        );

        Assert.Equal(
            """{"default_ip_pool_id":null}""",
            _handler.Requests.Single().Body!.ToJsonString()
        );
    }

    [Fact]
    public async Task ReceivingExplainsThatDkimMustVerifyFirst()
    {
        var provider = await Configured();
        _handler.Respond(
            HttpStatusCode.UnprocessableEntity,
            BirdFixtures.Error("E05019", "DKIM not verified.")
        );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.Create(
                new CreateRequest(
                    Props.Urn(Receiving),
                    Props.Of(("domainId", "dom_1")),
                    TimeSpan.Zero,
                    false
                ),
                default
            )
        );

        Assert.Equal(
            """{"inbound":{"enabled":true}}""",
            _handler.Requests.Single().Body!.ToJsonString()
        );
        Assert.Contains("depend on DomainVerification", error.Message);
    }

    [Fact]
    public async Task ReceivingTurnedOffOutsidePulumiReadsAsDeleted()
    {
        var provider = await Configured();
        _handler
            .Respond(
                HttpStatusCode.OK,
                BirdFixtures.Domain(receivingState: "enabled", inboundStatus: "verified")
            )
            .Respond(HttpStatusCode.OK, BirdFixtures.Domain(receivingState: "disabled"));
        var inputs = Props.Of(("domainId", "dom_1"));

        var enabled = await provider.Read(
            new ReadRequest(Props.Urn(Receiving), "dom_1/receiving", Props.Of(), inputs),
            default
        );
        var disabled = await provider.Read(
            new ReadRequest(Props.Urn(Receiving), "dom_1/receiving", Props.Of(), inputs),
            default
        );

        Assert.Equal("dom_1/receiving", enabled.Id);
        Assert.Equal(
            "verified",
            PropertyValues.GetString(ProviderTesting.Map(enabled.Outputs), "inboundStatus")
        );
        Assert.True(string.IsNullOrEmpty(disabled.Id));
    }

    [Fact]
    public async Task DeletingReceivingWaitsUntilBirdTurnsItOffAndRetriesFailures()
    {
        var provider = await Configured();
        _handler
            .Respond(
                HttpStatusCode.OK,
                BirdFixtures.Domain(receivingState: "disabling", inboundStatus: "verified")
            )
            .Respond(
                HttpStatusCode.OK,
                BirdFixtures.Domain(receivingState: "disable_failed", inboundStatus: "verified")
            )
            .Respond(
                HttpStatusCode.OK,
                BirdFixtures.Domain(receivingState: "disabling", inboundStatus: "verified")
            )
            .Respond(HttpStatusCode.OK, BirdFixtures.Domain(receivingState: "disabled"));
        var inputs = Props.Of(("domainId", "dom_1"));

        await provider.Delete(
            new DeleteRequest(
                Props.Urn(Receiving),
                "dom_1/receiving",
                inputs,
                TimeSpan.Zero,
                inputs
            ),
            default
        );

        Assert.Equal(
            [HttpMethod.Patch, HttpMethod.Get, HttpMethod.Patch, HttpMethod.Get],
            _handler.Requests.Select(request => request.Method)
        );
        Assert.Equal(3, _delays.Count);
    }

    [Fact]
    public async Task GetSendingDomainFindsAnExactMatchAcrossPages()
    {
        var provider = await Configured();
        _handler
            .Respond(
                HttpStatusCode.OK,
                new JsonObject
                {
                    ["data"] = new JsonArray(
                        BirdFixtures.Domain(id: "dom_9", domain: "old.mail.example.com")
                    ),
                    ["next_cursor"] = "c1",
                }
            )
            .Respond(
                HttpStatusCode.OK,
                new JsonObject
                {
                    ["data"] = new JsonArray(BirdFixtures.Domain(id: "dom_1")),
                    ["next_cursor"] = null,
                }
            );

        var response = await provider.Invoke(
            new InvokeRequest(
                BirdResourceProvider.GetSendingDomain,
                Props.Of(("domain", "mail.example.com"))
            ),
            default
        );

        Assert.Equal("dom_1", PropertyValues.GetString(ProviderTesting.Map(response.Return), "id"));
        Assert.Contains("name=mail.example.com", _handler.Requests[0].Uri.Query);
        Assert.Contains("starting_after=c1", _handler.Requests[1].Uri.Query);
    }

    private BirdResourceProvider Provider() =>
        new(
            _handler,
            key => _environment.GetValueOrDefault(key),
            (delay, _) =>
            {
                _delays.Add(delay);
                return Task.CompletedTask;
            }
        );

    private async Task<BirdResourceProvider> Configured(string apiKey = "bk_us1_test")
    {
        var provider = Provider();
        await provider.Configure(
            new ConfigureRequest(
                ImmutableDictionary<string, string>.Empty,
                Props.Of(("apiKey", new PropertyValue(apiKey).WithSecret(true))),
                acceptSecrets: true,
                acceptResources: false,
                sendsOldInputs: true,
                sendsOldInputsToDelete: true
            ),
            default
        );
        return provider;
    }

    private static CheckRequest ConfigCheck(params (string Key, object? Value)[] values) =>
        new(Props.Urn("pulumi:providers:bird"), Props.Of(), Props.Of(values), []);

    private static CheckRequest Check(string type, params (string Key, object? Value)[] values) =>
        new(Props.Urn(type), Props.Of(), Props.Of(values), []);

    private static DiffRequest Diff(
        string type,
        ImmutableDictionary<string, PropertyValue> olds,
        ImmutableDictionary<string, PropertyValue> news
    ) => new(Props.Urn(type), "id", olds, news, [], olds);

    private static ImmutableDictionary<string, PropertyValue> Outputs(
        IDictionary<string, PropertyValue>? values
    ) => values!.ToImmutableDictionary();
}
