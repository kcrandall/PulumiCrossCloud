using System.Collections.Immutable;
using Pulumi;
using Pulumi.Mailgun;
using Pulumi.Random;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Mailgun;

public class MailgunEmailService : IEmailService
{
    private readonly Domain _domain;

    public MailgunEmailService(
        string name,
        string domainName,
        IDnsZone? zone = null,
        Pulumi.Mailgun.Provider? provider = null
    )
    {
        Name = name;

        var resourceOptions = new CustomResourceOptions { Provider = provider };

        // Generate a random password for SMTP
        var smtpPassword = new RandomPassword(
            $"{name}-smtp-password",
            new RandomPasswordArgs
            {
                Length = 20,
                Special = true,
                OverrideSpecial = "_%@", // Safe special chars
            }
        );

        // Create the Mailgun domain
        _domain = new Domain(
            name,
            new DomainArgs
            {
                Name = domainName,
                SmtpPassword = smtpPassword.Result,
                // We specify a DKIM selector to ensure stable DNS record names if needed,
                // but Mailgun defaults are usually fine ("k1" or similar).
                // However, to extract specific records reliably without knowing the selector,
                // we rely on the SendingRecordsSets output which contains the full Name.
            },
            resourceOptions
        );

        // Use the generated password
        Password = new MailgunSecret(
            $"{name}-password",
            Output.Tuple(smtpPassword.Result, _domain.Id).Apply(values => values.Item1),
            _domain
        );

        SmtpEndpoint = Output.Create("smtp.mailgun.org");
        SmtpPort = Output.Create(587);
        Username = Output.Create($"postmaster@{domainName}");

        // Process DNS Records
        // Mailgun returns a list of records. We map each one to a DnsRecord.
        // Since the number of records is dynamic (Output), this is tricky for strict IaC if we want
        // to create specific resources for each.
        // However, we can create a fixed set of DnsRecord objects if we assume standard Mailgun setup:
        // 1. SPF (TXT)
        // 2. DKIM (TXT)
        // 3. Tracking (CNAME)
        // 4. MX (MX) - usually 2

        // BUT, `SendingRecordsSets` output gives us the exact list.
        // If we want to expose `IEnumerable<DnsRecord>`, we must return a concrete list of objects.
        // Each object must have a `Name` (string) and `Value` (Output).
        // If the `Name` is only known at runtime (Output), we can't create a DnsRecord (which expects string Name).
        // Mailgun `SendingRecordsSets` contains `Name` as a property.

        // Strategy: We can't use the dynamic list from Mailgun if `DnsRecord` requires a string Name.
        // Let's check DnsRecord definition: `string Name`. It is NOT Output<string>.
        // This means we MUST know the record names at construction time.

        // Known Mailgun Record Names for `domainName`:
        // 1. SPF: `domainName`
        // 2. MX: `domainName`
        // 3. CNAME (Tracking): `email.{domainName}` (default is "email" subdomain usually, configurable?)
        //    Actually Mailgun defaults tracking to "email" subdomain? No, we configure it.
        //    If we didn't configure it, it might not exist or be default.
        // 4. DKIM: `{selector}._domainkey.{domainName}`.

        // To make this work robustly with `DnsRecord`, we will assume standard configuration:
        // - SPF: TXT on `domainName`
        // - DKIM: TXT on `k1._domainkey.{domainName}` (We will assume selector 'k1' or we should have set it).
        // - MX: MX on `domainName`

        // Let's filter the SendingRecordsSets Output to find these specific values.

        var recordList = new List<DnsRecord>();

        // 1. SPF (TXT on root)
        // Extract Name and Value from the SendingRecordsSets list
        var spfRecord = _domain.SendingRecordsSets.Apply(records =>
            records.FirstOrDefault(r =>
                r != null
                && r.RecordType == "TXT"
                && (
                    r.Name == domainName
                    || r.Name == ""
                    || (r.Value != null && r.Value.StartsWith("v=spf1"))
                )
            )
        );

        recordList.Add(
            new DnsRecord(
                DnsRecordType.TXT,
                spfRecord.Apply(r => r?.Name ?? domainName),
                spfRecord.Apply(r => r?.Value ?? "v=spf1 include:mailgun.org ~all"),
                $"{domainName}-txt-spf"
            )
        );

        // 2. DKIM (TXT on k1._domainkey)
        var dkimRecord = _domain.SendingRecordsSets.Apply(records =>
            records.FirstOrDefault(r =>
                r != null
                && r.RecordType == "TXT"
                && r.Name != null
                && r.Name.Contains("_domainkey")
            )
        );

        recordList.Add(
            new DnsRecord(
                DnsRecordType.TXT,
                dkimRecord.Apply(r => r?.Name ?? $"k1._domainkey.{domainName}"),
                dkimRecord.Apply(r => r?.Value ?? ""),
                $"{domainName}-txt-dkim"
            )
        );

        // 3. MX (MX on root)
        var mxRecordA = _domain.SendingRecordsSets.Apply(records =>
            records.FirstOrDefault(r =>
                r != null && r.RecordType == "MX" && r.Value != null && r.Value.Contains("mxa")
            )
        );

        recordList.Add(
            new DnsRecord(
                DnsRecordType.MX,
                mxRecordA.Apply(r => r?.Name ?? domainName),
                mxRecordA.Apply(r => r?.Value ?? "10 mxa.mailgun.org"),
                $"{domainName}-mx-a"
            )
        );

        var mxRecordB = _domain.SendingRecordsSets.Apply(records =>
            records.FirstOrDefault(r =>
                r != null && r.RecordType == "MX" && r.Value != null && r.Value.Contains("mxb")
            )
        );

        recordList.Add(
            new DnsRecord(
                DnsRecordType.MX,
                mxRecordB.Apply(r => r?.Name ?? domainName),
                mxRecordB.Apply(r => r?.Value ?? "10 mxb.mailgun.org"),
                $"{domainName}-mx-b"
            )
        );

        // 4. CNAME (Tracking)
        var cnameRecord = _domain.SendingRecordsSets.Apply(records =>
            records.FirstOrDefault(r => r != null && r.RecordType == "CNAME")
        );

        recordList.Add(
            new DnsRecord(
                DnsRecordType.CNAME,
                cnameRecord.Apply(r => r?.Name ?? $"email.{domainName}"),
                cnameRecord.Apply(r => r?.Value ?? "mailgun.org"),
                $"{domainName}-cname-tracking"
            )
        );

        DnsRecords = recordList;

        if (zone != null)
            zone.RegisterRecords(DnsRecords);
    }

    public string Name { get; }

    public Output<string> Arn => _domain.Id; // Using ID as ARN for Mailgun

    public CloudProvider Provider => CloudProvider.Mailgun;

    public Output<string> SmtpEndpoint { get; }

    public Output<int> SmtpPort { get; }

    public Output<string> Username { get; }

    public ISecret Password { get; }

    public IEnumerable<DnsRecord> DnsRecords { get; }

    public IEnumerable<IPolicyStatement> GetPolicy(params EmailPermissions[] permissions)
    {
        var policies = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            if (permission == EmailPermissions.Send)
            {
                // To send, we need access to the password.
                policies.AddRange(Password.GetPolicy(SecretPermissions.Read));
            }
        }
        return policies;
    }

    /// <summary>
    /// A simple wrapper to expose a Pulumi Output as an ISecret.
    /// Does not persist to a cloud secret store, just holds the value in Pulumi state.
    /// </summary>
    private class MailgunSecret : ISecret
    {
        private readonly Output<string> _value;

        public MailgunSecret(string name, Output<string> value, Resource resource)
        {
            Name = name;
            _value = Output.CreateSecret(value);
            Resource = resource;
        }

        public Output<string?> Value => _value.Apply(v => (string?)v);

        public bool IgnoreChangesAfterCreate => false;

        public Resource Resource { get; }

        public string Name { get; }

        public Output<string> Arn => Output.Create($"virtual-secret:{Name}");

        public CloudProvider Provider => CloudProvider.Mailgun;

        public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions)
        {
            // No IAM policies needed for an in-memory/output secret.
            return Enumerable.Empty<IPolicyStatement>();
        }

        public Task<string> GetValueAsync()
        {
            throw new NotSupportedException("Cannot get value async from Output.");
        }

        public void SetValue(string value)
        {
            throw new NotSupportedException("Cannot set value of read-only Mailgun secret.");
        }
    }
}
