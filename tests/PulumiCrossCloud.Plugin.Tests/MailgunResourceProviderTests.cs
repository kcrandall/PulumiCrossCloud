using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.Mailgun;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class MailgunResourceProviderTests
{
    private const string Verification = PulumiCrossCloud
        .Plugin
        .Mailgun
        .DomainVerificationResource
        .Type;

    private readonly FakeApiHandler _handler = new();
    private readonly List<TimeSpan> _delays = [];

    [Fact]
    public async Task VerificationPollsUntilTheDomainIsActive()
    {
        var provider = await Configured(region: "eu");
        _handler
            .Respond(HttpStatusCode.OK, Domain("unverified"))
            .Respond(HttpStatusCode.OK, Domain("active"));

        var response = await provider.Create(
            new CreateRequest(
                Props.Urn(Verification),
                Props.Of(("domain", "mail.example.com")),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Equal("mail.example.com/verification", response.Id);
        var request = _handler.Requests[0];
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(
            "https://api.eu.mailgun.net/v4/domains/mail.example.com/verify",
            request.Uri.ToString()
        );
        Assert.Equal(
            $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("api:key-123"))}",
            request.Authorization
        );
        Assert.Equal([TimeSpan.FromSeconds(10)], _delays);
        Assert.Equal(
            "active",
            PropertyValues.GetString(ProviderTesting.Map(response.Outputs), "state")
        );
    }

    [Fact]
    public async Task VerificationTimesOutListingInvalidRecords()
    {
        var provider = await Configured();
        for (var i = 0; i < 2; i++)
            _handler.Respond(HttpStatusCode.OK, Domain("unverified"));

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            provider.Create(
                new CreateRequest(
                    Props.Urn(Verification),
                    Props.Of(("domain", "mail.example.com")),
                    TimeSpan.FromSeconds(15),
                    false
                ),
                default
            )
        );

        Assert.Contains(
            "TXT mail.example.com: invalid (expected v=spf1 include:mailgun.org ~all)",
            error.Message
        );
        Assert.DoesNotContain("k1._domainkey", error.Message);
    }

    [Fact]
    public async Task DisabledDomainsFailImmediately()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Domain("disabled"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.Create(
                new CreateRequest(
                    Props.Urn(Verification),
                    Props.Of(("domain", "mail.example.com")),
                    TimeSpan.Zero,
                    false
                ),
                default
            )
        );
        Assert.Empty(_delays);
    }

    [Fact]
    public async Task CheckConfigRejectsUnknownRegions()
    {
        var provider = new MailgunResourceProvider(_handler, _ => "key-from-env");

        var response = await provider.CheckConfig(
            new CheckRequest(
                Props.Urn("pulumi:providers:crosscloud-mailgun"),
                Props.Of(),
                Props.Of(("region", "ap")),
                []
            ),
            default
        );

        Assert.Equal("region", Assert.Single(response.Failures!).Property);
    }

    private async Task<MailgunResourceProvider> Configured(string region = "us")
    {
        var provider = new MailgunResourceProvider(
            _handler,
            _ => null,
            ProviderTesting.RecordDelays(_delays)
        );
        await provider.Configure(
            ProviderTesting.Configure(("apiKey", "key-123"), ("region", region)),
            default
        );
        return provider;
    }

    private static JsonObject Domain(string state) =>
        new()
        {
            ["message"] = "Domain DNS records have been updated",
            ["domain"] = new JsonObject
            {
                ["id"] = "d1",
                ["name"] = "mail.example.com",
                ["state"] = state,
                ["is_disabled"] = state == "disabled",
            },
            ["sending_dns_records"] = new JsonArray(
                new JsonObject
                {
                    ["record_type"] = "TXT",
                    ["name"] = "mail.example.com",
                    ["value"] = "v=spf1 include:mailgun.org ~all",
                    ["valid"] = "invalid",
                },
                new JsonObject
                {
                    ["record_type"] = "TXT",
                    ["name"] = "k1._domainkey.mail.example.com",
                    ["value"] = "k=rsa; p=abc",
                    ["valid"] = "valid",
                }
            ),
        };
}
