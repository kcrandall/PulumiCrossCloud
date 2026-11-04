using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Bird;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class BirdEmailServiceTests
{
    [Fact]
    public async Task PublishesBirdRecordsAndWaitsForVerificationBeforeExposingThePassword()
    {
        var mocks = new BirdMocks();
        TestZone? zone = null;
        BirdEmailService? email = null;
        Task<string?>? password = null;
        Task<string>? smtpHost = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                zone = new TestZone();
                email = new BirdResourceFactory(
                    Output.CreateSecret("bk_us1_key")
                ).CreateEmailService(
                    "email",
                    "mail.example.com",
                    zone,
                    new BirdEmailServiceOptions()
                );
                zone.Capture();
                password = email.Password.Value.GetValueAsync();
                smtpHost = email.SmtpEndpoint.GetValueAsync();
            }
        );

        Assert.Equal("us1.smtp.bird.com", await smtpHost!);
        Assert.Equal("bk_us1_key", await password!);
        Assert.True(await Output.IsSecretAsync(email!.Password.Value));
        Assert.Same(email.Verification, email.Password.ValueResource);

        var records = await Task.WhenAll(zone!.Records);
        Assert.Equal(
            [
                ("TXT", "bird1._domainkey.mail.example.com", "v=DKIM1; p=abc"),
                ("CNAME", "send.mail.example.com", "rp.us1.bird.com"),
                ("TXT", "_dmarc.mail.example.com", "v=DMARC1; p=none;"),
                ("CNAME", "links.mail.example.com", "track.us1.bird.com"),
            ],
            records
        );

        var verification = mocks.Resources.Single(resource =>
            resource.Type == DomainVerification.TypeToken
        );
        Assert.Equal("email-verification", verification.Name);
        Assert.True((bool)verification.Inputs["waitForSending"]);
        var provider = mocks.Resources.Single(resource =>
            resource.Type == "pulumi:providers:crosscloud-bird"
        );
        Assert.Equal("us1", provider.Inputs["region"]);
    }

    [Fact]
    public async Task OptionsSkipDisabledRecordsAndVerificationWithoutAZone()
    {
        var mocks = new BirdMocks();
        BirdEmailService? email = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                email = new BirdEmailService(
                    "email",
                    "mail.example.com",
                    Output.CreateSecret("bk_eu1_key"),
                    BirdRegion.Eu1,
                    options: new BirdEmailServiceOptions
                    {
                        TrackingName = "",
                        PublishDmarcRecord = false,
                    }
                );
            }
        );

        Assert.Equal(
            ["mail.example.com-dkim", "mail.example.com-return-path"],
            email!.DnsRecords.Select(record => record.Key)
        );
        Assert.Null(email.Verification);
        Assert.Same(email.Domain, email.Password.ValueResource);
        Assert.Equal(
            "",
            mocks.Resources.Single(r => r.Type == SendingDomain.TypeToken).Inputs["trackingName"]
        );
    }

    [Fact]
    public void RecordSelectionPrefersStagedChangesAndIgnoresDeprecatedRecords()
    {
        SendingDomainDnsRecord[] records =
        [
            new(
                "CNAME",
                "old",
                "old.mail.example.com",
                "a",
                "return_path",
                "deprecated",
                false,
                "verified"
            ),
            new(
                "CNAME",
                "send",
                "send.mail.example.com",
                "b",
                "return_path",
                "active",
                false,
                "verified"
            ),
            new(
                "CNAME",
                "bounce",
                "bounce.mail.example.com",
                "c",
                "return_path",
                "pending",
                false,
                "pending"
            ),
        ];

        Assert.Equal(
            "bounce.mail.example.com",
            BirdDnsRecords.Select(records, "return_path")!.Host
        );
        Assert.Equal(
            "send.mail.example.com",
            BirdDnsRecords.Select(records[..2], "return_path")!.Host
        );
        Assert.Null(BirdDnsRecords.Select(records, "dkim"));
        Assert.Throws<InvalidOperationException>(() =>
            BirdDnsRecords.Require(records, "dkim", "mail.example.com")
        );
    }

    [Fact]
    public void PluginVersionIsThePackageVersionWithoutBuildMetadata()
    {
        Assert.DoesNotContain('+', CrossCloudPlugins.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", CrossCloudPlugins.Version);
    }

    private sealed class BirdMocks : IMocks
    {
        public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            Resources.Enqueue(args);
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (args.Type == SendingDomain.TypeToken)
            {
                state["status"] = "pending";
                state["dnsRecords"] = ImmutableArray.Create<object>(
                    Record("dkim", "TXT", "bird1._domainkey.mail.example.com", "v=DKIM1; p=abc"),
                    Record("return_path", "CNAME", "send.mail.example.com", "rp.us1.bird.com"),
                    Record("tracking", "CNAME", "links.mail.example.com", "track.us1.bird.com"),
                    Record("inbound_mx", "MX", "mail.example.com", "10 mx.us1.bird.com")
                );
                return Task.FromResult<(string? id, object state)>(("dom_1", state));
            }

            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
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
                .Add("optional", purpose == "inbound_mx")
                .Add("status", "pending");
    }

    private sealed class TestZone : IDnsZone
    {
        private readonly List<DnsRecord> _registered = [];

        public List<Task<(string Type, string Name, string Value)>> Records { get; } = [];

        public string DomainName => "example.com";
        public Output<string> ZoneId => Output.Create("zone");
        public Output<ImmutableArray<string>> NameServers =>
            Output.Create(ImmutableArray<string>.Empty);
        public Resource Resource { get; } = new TestRecordResource("zone");
        public string Name => "zone";
        public Output<string> Arn => ZoneId;
        public CloudProvider Provider => CloudProvider.Other;

        public IDnsRegistration RegisterRecord(
            DnsRecord record,
            int? ttl = null,
            bool proxied = false
        )
        {
            _registered.Add(record);
            return new TestRegistration(record, this);
        }

        public IEnumerable<IDnsRegistration> RegisterRecords(
            IEnumerable<DnsRecord> records,
            int? ttl = null,
            bool proxied = false
        ) => records.Select(record => RegisterRecord(record, ttl, proxied)).ToList();

        public void Capture() =>
            Records.AddRange(
                _registered.Select(record =>
                    Output
                        .Tuple(record.Name, record.Value)
                        .Apply(values => (record.Type.ToString(), values.Item1, values.Item2))
                        .GetValueAsync()
                )
            );
    }

    private sealed class TestRegistration(DnsRecord record, IDnsZone zone) : IDnsRegistration
    {
        public DnsRecord Record { get; } = record;
        public int? Ttl => null;
        public bool Proxied => false;
        public IDnsZone Zone { get; } = zone;
        public Resource Resource { get; } = new TestRecordResource(record.Key);
        public string Name => Record.Key;
        public Output<string> Arn => Output.Create(Record.Key);
        public CloudProvider Provider => CloudProvider.Other;
    }

    private sealed class TestRecordResource(string name)
        : ComponentResource("test:index:Record", name);
}

internal static class OutputTestExtensions
{
    public static Task<T> GetValueAsync<T>(this Output<T> output)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        output.Apply(value =>
        {
            completion.TrySetResult(value);
            return value;
        });
        return completion.Task;
    }
}
