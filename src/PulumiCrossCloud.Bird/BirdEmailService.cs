using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// Sends email through Bird's SMTP relay from a verified sending domain.
/// Creates the Bird domain, optionally publishes its DKIM, return-path, DMARC and tracking records
/// in a DNS zone, waits for Bird to verify it, and can enable inbound mail. SMTP uses the literal username <c>bird</c>
/// with an API key as the password.
/// </summary>
public class BirdEmailService : IEmailService
{
    public BirdEmailService(
        string name,
        string domainName,
        Input<string> smtpApiKey,
        BirdRegion region = BirdRegion.Us1,
        IDnsZone? zone = null,
        BirdProvider? provider = null,
        BirdEmailServiceOptions? options = null
    )
    {
        options ??= new BirdEmailServiceOptions();
        Name = name;
        var receivingEnabled = options.ReceivingEnabled || options.InboundMail is not null;

        Domain = new SendingDomain(
            name,
            new SendingDomainArgs
            {
                Domain = domainName,
                ReturnPathName = Optional(options.ReturnPathName),
                TrackingName = Optional(options.TrackingName),
                DefaultIpPoolId = Optional(options.DefaultIpPoolId),
            },
            new CustomResourceOptions { Provider = provider }
        );

        // Record names are only known after creation, but the set of records is fixed, so each
        // gets a stable key for its DNS resource.
        List<DnsRecord> records =
        [
            Record(DnsRecordType.TXT, BirdDnsRecords.Dkim, domainName),
            Record(DnsRecordType.CNAME, BirdDnsRecords.ReturnPath, domainName),
        ];
        if (options.PublishDmarcRecord)
        {
            var dmarc = Domain.DnsRecords.Apply(all =>
                BirdDnsRecords.Select(all, BirdDnsRecords.Dmarc)
            );
            records.Add(
                new DnsRecord(
                    DnsRecordType.TXT,
                    dmarc.Apply(record => record?.Host ?? $"_dmarc.{domainName}"),
                    dmarc.Apply(record => record?.Value ?? BirdDnsRecords.DefaultDmarcPolicy),
                    $"{domainName}-dmarc"
                )
            );
        }
        if (options.TrackingName != "")
            records.Add(Record(DnsRecordType.CNAME, BirdDnsRecords.Tracking, domainName));
        if (receivingEnabled)
            for (var index = 0; index < options.InboundMxRecordCount; index++)
                records.Add(InboundMxRecord(index, options.InboundMxRecordCount, domainName));
        DnsRecords = records;

        var registrations = zone?.RegisterRecords(DnsRecords).ToList() ?? [];
        DnsRegistrations = registrations;

        if (options.WaitForVerification ?? zone is not null)
        {
            var verificationArgs = new DomainVerificationArgs
            {
                DomainId = Domain.Id,
                WaitForSending = true,
            };
            foreach (var record in DnsRecords)
                verificationArgs.Triggers.Add(record.Value);

            Verification = new DomainVerification(
                $"{name}-verification",
                verificationArgs,
                new CustomResourceOptions
                {
                    Provider = provider,
                    DependsOn = registrations
                        .Select(registration => registration.Resource)
                        .ToList(),
                }
            );
        }

        if (receivingEnabled)
        {
            // Bird only enables receiving once DKIM verifies, and checks the MX records after.
            List<Resource> dependencies = [Verification is null ? Domain : Verification];
            dependencies.AddRange(registrations.Select(registration => registration.Resource));
            Receiving = new DomainReceiving(
                $"{name}-receiving",
                new DomainReceivingArgs { DomainId = Domain.Id },
                new CustomResourceOptions { Provider = provider, DependsOn = dependencies }
            );
        }

        if (options.InboundMail is { } inboundMail)
        {
            // Webhook endpoints are workspace wide; handlers should check the recipient domain.
            InboundWebhook = new Webhook(
                $"{name}-inbound-webhook",
                new WebhookArgs
                {
                    Url = inboundMail.WebhookUrl,
                    Events = ["email.received"],
                    Description = $"Inbound mail for {domainName}",
                },
                new CustomResourceOptions { Provider = provider, DependsOn = [Receiving!] }
            );
            InboundMail = new InboundMailEndpoint(
                InboundWebhook.Url,
                InboundWebhook.Secret,
                InboundWebhook
            );
        }

        SmtpEndpoint = Output.Create(region.SmtpHost());
        SmtpPort = Output.Create(options.SmtpPort);
        Username = Output.Create("bird");
        Resource ready = Verification is null ? Domain : Verification;
        Password = new BirdSecret(
            $"{name}-password",
            Output
                .Tuple(smtpApiKey.Apply(key => key), ((CustomResource)ready).Id)
                .Apply(values => values.Item1),
            Domain,
            ready
        );
    }

    public string Name { get; }

    public SendingDomain Domain { get; }

    /// <summary>
    /// Waits for Bird to verify the domain; null when verification waiting is disabled.
    /// </summary>
    public DomainVerification? Verification { get; }

    /// <summary>
    /// Inbound mail on the domain; null unless <see cref="BirdEmailServiceOptions.ReceivingEnabled"/>
    /// or <see cref="BirdEmailServiceOptions.InboundMail"/> is set.
    /// </summary>
    public DomainReceiving? Receiving { get; }

    /// <summary>
    /// The <c>email.received</c> webhook; null unless <see cref="BirdEmailServiceOptions.InboundMail"/> is set.
    /// </summary>
    public Webhook? InboundWebhook { get; }

    public InboundMailEndpoint? InboundMail { get; }

    public IReadOnlyList<IDnsRegistration> DnsRegistrations { get; }

    public Output<string> Arn => Domain.Id;

    public CloudProvider Provider => CloudProvider.Bird;

    public Output<string> SmtpEndpoint { get; }

    public Output<int> SmtpPort { get; }

    public Output<string> Username { get; }

    /// <summary>
    /// The SMTP API key. Its <see cref="ISecret.ValueResource"/> is the verification resource when
    /// present, so consumers start only after the domain can send.
    /// </summary>
    public ISecret Password { get; }

    public IEnumerable<DnsRecord> DnsRecords { get; }

    public IEnumerable<IPolicyStatement> GetPolicy(params EmailPermissions[] permissions) =>
        permissions.Contains(EmailPermissions.Send)
            ? Password.GetPolicy(SecretPermissions.Read)
            : [];

    private static Input<string>? Optional(string? value) =>
        value is { } text ? (Input<string>)text : null;

    private DnsRecord InboundMxRecord(int index, int expectedCount, string domainName)
    {
        var record = Domain.DnsRecords.Apply(all =>
        {
            var mx = BirdDnsRecords.SelectAll(all, BirdDnsRecords.InboundMx);
            if (mx.Count != expectedCount)
                throw new InvalidOperationException(
                    $"Bird reports {mx.Count} inbound MX records for {domainName}, but "
                        + $"InboundMxRecordCount is {expectedCount}. Set it to {mx.Count}."
                );
            return mx[index];
        });
        return new DnsRecord(
            DnsRecordType.MX,
            record.Apply(r => r.Host),
            record.Apply(r => r.Value),
            $"{domainName}-inbound-mx-{index + 1}"
        );
    }

    private DnsRecord Record(DnsRecordType type, string purpose, string domainName)
    {
        var record = Domain.DnsRecords.Apply(all =>
            BirdDnsRecords.Require(all, purpose, domainName)
        );
        return new DnsRecord(
            type,
            record.Apply(r => r.Host),
            record.Apply(r => r.Value),
            $"{domainName}-{purpose.Replace('_', '-')}"
        );
    }

    /// <summary>
    /// Exposes the SMTP API key as an <see cref="ISecret"/> held in Pulumi state only.
    /// </summary>
    private sealed class BirdSecret(
        string name,
        Output<string> value,
        Resource resource,
        Resource? valueResource
    ) : ISecret
    {
        private readonly Output<string> _value = Output.CreateSecret(value);

        public Output<string?> Value => _value.Apply(v => (string?)v);

        public bool IgnoreChangesAfterCreate => false;

        public Resource Resource { get; } = resource;

        public Resource ValueResource { get; } = valueResource ?? resource;

        public string Name { get; } = name;

        public Output<string> Arn => Output.Create($"virtual-secret:{Name}");

        public CloudProvider Provider => CloudProvider.Bird;

        public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions) =>
            [];

        public Task<string> GetValueAsync() =>
            throw new NotSupportedException("Cannot get value async from Output.");

        public void SetValue(string value) =>
            throw new NotSupportedException("Cannot set value of read-only Bird secret.");
    }
}
