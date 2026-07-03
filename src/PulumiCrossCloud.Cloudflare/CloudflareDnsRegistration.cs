using Pulumi;
using Pulumi.Cloudflare;
using PulumiCrossCloud.Core;
using CoreDnsRecord = PulumiCrossCloud.Core.DnsRecord;
using CfDnsRecord = Pulumi.Cloudflare.DnsRecord;

namespace PulumiCrossCloud.Cloudflare;

/// <summary>
/// CloudFlare implementation of IDnsRegistration.
/// Represents a DNS record registered in a CloudFlare zone.
/// </summary>
public class CloudflareDnsRegistration : IDnsRegistration
{
    private readonly CfDnsRecord _record;

    public CloudflareDnsRegistration(
        string name,
        IDnsZone zone,
        CoreDnsRecord record,
        int? ttl = null,
        bool proxied = false,
        Provider? provider = null
    )
    {
        Name = name;
        Zone = zone;
        Record = record;
        Ttl = ttl;
        Proxied = proxied;

        // Determine if this record type can be proxied
        // Only A, AAAA, and CNAME records can be proxied in CloudFlare
        var canBeProxied =
            record.Type is DnsRecordType.A or DnsRecordType.AAAA or DnsRecordType.CNAME;
        var actualProxied = proxied && canBeProxied;

        var options = provider is not null ? new CustomResourceOptions { Provider = provider } : null;

        // Handle provider-specific field requirements
        var typeStr = MapRecordType(record.Type);
        var content = record.Value;
        Pulumi.Input<double>? priority = null;

        if (record.Type == DnsRecordType.MX)
        {
            // Expect values like: "10 mxa.mailgun.org"
            // Split into priority and hostname for Cloudflare
            priority = content.Apply(v =>
            {
                var parts = (v ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0], out var p)) return (double)p;
                return 10.0; // reasonable default if not provided
            });
            content = content.Apply(v =>
            {
                var parts = (v ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 2 ? parts[1] : (v ?? string.Empty);
            });
        }

        _record = new CfDnsRecord(
            name,
            new DnsRecordArgs
            {
                ZoneId = zone.ZoneId,
                Name = record.Name,
                Content = content.Apply(s => s ?? string.Empty),
                Type = typeStr,
                Ttl = actualProxied ? 1 : (ttl ?? 300), // TTL must be 1 for proxied records
                Proxied = actualProxied,
                Priority = priority
            },
            options
        );
    }

    public string Name { get; }
    public CoreDnsRecord Record { get; }
    public int? Ttl { get; }
    public bool Proxied { get; }
    public IDnsZone Zone { get; }
    public CloudProvider Provider => CloudProvider.CloudFlare;
    public Output<string> Arn => _record.Id;
    public Resource Resource => _record;

    private static string MapRecordType(DnsRecordType type) =>
        type switch
        {
            DnsRecordType.A => "A",
            DnsRecordType.AAAA => "AAAA",
            DnsRecordType.CNAME => "CNAME",
            DnsRecordType.TXT => "TXT",
            DnsRecordType.MX => "MX",
            DnsRecordType.SRV => "SRV",
            DnsRecordType.NS => "NS",
            DnsRecordType.CAA => "CAA",
            DnsRecordType.PTR => "PTR",
            _ => throw new ArgumentException($"Unsupported DNS record type: {type}")
        };
}
