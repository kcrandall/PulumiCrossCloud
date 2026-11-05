using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Bird;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Mailgun;
using PulumiCrossCloud.PostHog;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class EmailServiceLibraryTests
{
    [Fact]
    public async Task BirdReceivingPublishesEachMxRecordAndWaitsForVerification()
    {
        var mocks = new Mocks(inboundMxCount: 3);
        BirdEmailService? email = null;
        Task<string[]>? mxValues = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                email = new BirdEmailService(
                    "email",
                    "inbound.example.com",
                    Output.CreateSecret("bk_us1_key"),
                    zone: new RecordingZone(),
                    options: new BirdEmailServiceOptions { ReceivingEnabled = true }
                );
                mxValues = Output
                    .All(
                        email
                            .DnsRecords.Where(record => record.Type == DnsRecordType.MX)
                            .Select(record => record.Value)
                    )
                    .Apply(values => values.ToArray())
                    .GetValueAsync();
            }
        );

        Assert.Equal(
            ["10 mx1.us1.bird.test", "20 mx2.us1.bird.test", "30 mx3.us1.bird.test"],
            await mxValues!
        );
        Assert.NotNull(email!.Receiving);
        Assert.Contains(
            mocks.Resources,
            resource =>
                resource.Type == DomainReceiving.TypeToken && resource.Name == "email-receiving"
        );
        // Sending readiness does not wait for inbound mail.
        Assert.Same(email.Verification, ((IEmailService)email).Ready);
    }

    [Fact]
    public async Task BirdReceivingFailsClearlyWhenTheMxCountDiffers()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            Deployment.TestAsync(
                new Mocks(inboundMxCount: 2),
                new TestOptions { IsPreview = false },
                () =>
                {
                    _ = new BirdEmailService(
                        "email",
                        "inbound.example.com",
                        Output.CreateSecret("bk_us1_key"),
                        zone: new RecordingZone(),
                        options: new BirdEmailServiceOptions { ReceivingEnabled = true }
                    );
                }
            )
        );

        Assert.Contains("InboundMxRecordCount is 3. Set it to 2.", error.ToString());
    }

    [Fact]
    public async Task MailgunWaitsForVerificationWhenItPublishesDns()
    {
        var mocks = new Mocks();
        MailgunEmailService? email = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
                email = (MailgunEmailService)
                    new MailgunResourceFactory(Output.CreateSecret("key-123")).CreateEmailService(
                        "email",
                        "mail.example.com",
                        new RecordingZone()
                    )
        );

        Assert.NotNull(email!.Verification);
        Assert.Same(email.Verification, ((IEmailService)email).Ready);
        var verification = mocks.Resources.Single(resource =>
            resource.Type == MailgunDomainVerification.TypeToken
        );
        Assert.Equal("mail.example.com", verification.Inputs["domain"]);
        Assert.Contains(
            mocks.Resources,
            resource => resource.Type == "pulumi:providers:crosscloud-mailgun"
        );
    }

    [Fact]
    public async Task MailgunWithoutAZoneKeepsTheDomainAsTheReadyResource()
    {
        MailgunEmailService? email = null;
        await Deployment.TestAsync(
            new Mocks(),
            new TestOptions { IsPreview = false },
            () => email = new MailgunEmailService("email", "mail.example.com")
        );

        Assert.Null(email!.Verification);
        Assert.IsType<Pulumi.Mailgun.Domain>(((IEmailService)email).Ready);
    }

    [Fact]
    public async Task PostHogOrganizationUsesTheNativeProvider()
    {
        var mocks = new Mocks();
        Task<string>? slug = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                var organization = new PostHogOrganization(
                    "analytics",
                    new PostHogOrganizationArgs
                    {
                        ApiKey = Output.CreateSecret("phx_key"),
                        Name = "Acme",
                        Host = "https://eu.posthog.com",
                        DeleteOnDestroy = true,
                    }
                );
                slug = organization.Slug.GetValueAsync();
            }
        );

        var resource = mocks.Resources.Single(r => r.Type == PostHogOrganizationResource.TypeToken);
        Assert.Equal("Acme", resource.Inputs["name"]);
        Assert.True((bool)resource.Inputs["deleteOnDestroy"]);
        var provider = mocks.Resources.Single(r => r.Type == "pulumi:providers:crosscloud-posthog");
        Assert.Equal("https://eu.posthog.com", provider.Inputs["host"]);
        Assert.Equal("acme", await slug!);
        Assert.DoesNotContain(mocks.Resources, r => r.Type?.StartsWith("command:") == true);
    }

    [Fact]
    public async Task InboundMailThroughBirdSubscribesAnEmailReceivedWebhook()
    {
        var mocks = new Mocks(inboundMxCount: 3);
        IEmailService? email = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
                email = new BirdResourceFactory(Output.CreateSecret("bk_us1_key")).CreateEmailService(
                    "email",
                    "inbound.example.com",
                    new RecordingZone(),
                    new InboundMailOptions { WebhookUrl = "https://app.example.com/inbound" }
                )
        );

        Assert.Contains(mocks.Resources, r => r.Type == DomainReceiving.TypeToken);
        var webhook = mocks.Resources.Single(r => r.Type == Webhook.TypeToken);
        Assert.Equal("https://app.example.com/inbound", webhook.Inputs["url"]);
        Assert.Equal(["email.received"], ((IEnumerable<object>)webhook.Inputs["events"]).Cast<string>());
        Assert.NotNull(email!.InboundMail);
        Assert.NotNull(email.InboundMail!.SigningSecret);
        Assert.Contains(email.DnsRecords, record => record.Type == DnsRecordType.MX);
    }

    [Fact]
    public async Task InboundMailThroughMailgunForwardsTheDomainWithARoute()
    {
        var mocks = new Mocks();
        IEmailService? email = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
                email = new MailgunResourceFactory(Output.CreateSecret("key-123")).CreateEmailService(
                    "email",
                    "inbound.example.com",
                    null,
                    new InboundMailOptions { WebhookUrl = "https://app.example.com/inbound" }
                )
        );

        var route = mocks.Resources.Single(r => r.Type == "mailgun:index/route:Route");
        Assert.Equal("match_recipient(\".*@inbound.example.com\")", route.Inputs["expression"]);
        Assert.Equal(
            ["forward(\"https://app.example.com/inbound\")", "stop()"],
            ((IEnumerable<object>)route.Inputs["actions"]).Cast<string>()
        );
        Assert.NotNull(email!.InboundMail);
        Assert.Null(email.InboundMail!.SigningSecret);
    }

    [Fact]
    public async Task MailgunOptionsSetTrackingAndADedicatedSmtpLogin()
    {
        var mocks = new Mocks();
        MailgunEmailService? email = null;
        Task<string>? username = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                email = new MailgunResourceFactory(Output.CreateSecret("key-123"))
                {
                    EmailServiceOptions = new MailgunEmailServiceOptions
                    {
                        ClickTracking = true,
                        OpenTracking = false,
                        SmtpLogin = "app",
                    },
                }.CreateEmailService(
                    "email",
                    "mail.example.com",
                    null,
                    new MailgunEmailServiceOptions { ClickTracking = true, SmtpLogin = "app" }
                );
                username = email.Username.GetValueAsync();
            }
        );

        var domain = mocks.Resources.Single(r => r.Type == "mailgun:index/domain:Domain");
        Assert.True((bool)domain.Inputs["clickTracking"]);
        Assert.False(domain.Inputs.ContainsKey("openTracking"));
        var credential = mocks.Resources.Single(r =>
            r.Type == "mailgun:index/domainCredential:DomainCredential"
        );
        Assert.Equal("app", credential.Inputs["login"]);
        Assert.Equal("mail.example.com", credential.Inputs["domain"]);
        Assert.Equal("app@mail.example.com", await username!);
        Assert.NotNull(email!.SmtpCredential);
    }

    [Fact]
    public async Task MailgunDefaultsKeepThePostmasterLogin()
    {
        var mocks = new Mocks();
        Task<string>? username = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () => username = new MailgunEmailService("email", "mail.example.com").Username.GetValueAsync()
        );

        Assert.Equal("postmaster@mail.example.com", await username!);
        Assert.DoesNotContain(mocks.Resources, r => r.Type == "mailgun:index/domainCredential:DomainCredential");
        var domain = mocks.Resources.Single(r => r.Type == "mailgun:index/domain:Domain");
        Assert.False(domain.Inputs.ContainsKey("clickTracking"));
    }

    private sealed class Mocks(int inboundMxCount = 1) : IMocks
    {
        public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            Resources.Enqueue(args);
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            switch (args.Type)
            {
                case SendingDomain.TypeToken:
                    var domain = (string)args.Inputs["domain"];
                    List<object> records =
                    [
                        Record("dkim", "TXT", $"bird1._domainkey.{domain}", "v=DKIM1; p=abc"),
                        Record("return_path", "CNAME", $"send.{domain}", "rp.us1.bird.test"),
                        Record("tracking", "CNAME", $"links.{domain}", "track.us1.bird.test"),
                    ];
                    // Listed out of order to show the selection is stable.
                    for (var index = inboundMxCount; index >= 1; index--)
                        records.Add(
                            Record(
                                "inbound_mx",
                                "MX",
                                domain,
                                $"{index * 10} mx{index}.us1.bird.test"
                            )
                        );
                    state["dnsRecords"] = records.ToImmutableArray();
                    return Task.FromResult<(string?, object)>(("dom_1", state));
                case "mailgun:index/domain:Domain":
                    state["sendingRecordsSets"] = Array.Empty<object>();
                    break;
                case "random:index/randomPassword:RandomPassword":
                    state["result"] = "generated";
                    break;
                case Webhook.TypeToken:
                    state["status"] = "active";
                    state["paused"] = false;
                    state["secret"] = "whsec_test";
                    return Task.FromResult<(string?, object)>(("whk_1", state));
                case PostHogOrganizationResource.TypeToken:
                    state["slug"] = "acme";
                    state["adopted"] = false;
                    return Task.FromResult<(string?, object)>(("org_1", state));
            }

            return Task.FromResult<(string?, object)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(new Dictionary<string, object>());

        private static ImmutableDictionary<string, object> Record(
            string purpose,
            string type,
            string host,
            string value
        ) =>
            ImmutableDictionary<string, object>
                .Empty.Add("type", type)
                .Add("name", host)
                .Add("host", host)
                .Add("value", value)
                .Add("purpose", purpose)
                .Add("state", "active")
                .Add("optional", purpose is "inbound_mx" or "tracking")
                .Add("status", "pending");
    }

    private sealed class RecordingZone : IDnsZone
    {
        public string DomainName => "example.com";
        public Output<string> ZoneId => Output.Create("zone");
        public Output<ImmutableArray<string>> NameServers =>
            Output.Create(ImmutableArray<string>.Empty);
        public Resource Resource { get; } = new Marker("zone");
        public string Name => "zone";
        public Output<string> Arn => ZoneId;
        public CloudProvider Provider => CloudProvider.Other;

        public IDnsRegistration RegisterRecord(
            DnsRecord record,
            int? ttl = null,
            bool proxied = false
        ) => new Registration(record, this);

        public IEnumerable<IDnsRegistration> RegisterRecords(
            IEnumerable<DnsRecord> records,
            int? ttl = null,
            bool proxied = false
        ) => records.Select(record => RegisterRecord(record, ttl, proxied)).ToList();
    }

    private sealed class Registration(DnsRecord record, IDnsZone zone) : IDnsRegistration
    {
        public DnsRecord Record { get; } = record;
        public int? Ttl => null;
        public bool Proxied => false;
        public IDnsZone Zone { get; } = zone;
        public Resource Resource { get; } = new Marker(record.Key);
        public string Name => Record.Key;
        public Output<string> Arn => Output.Create(Record.Key);
        public CloudProvider Provider => CloudProvider.Other;
    }

    private sealed class Marker(string name) : ComponentResource("test:index:Marker", name);
}
