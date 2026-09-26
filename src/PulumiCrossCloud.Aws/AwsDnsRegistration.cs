using Pulumi;
using Pulumi.Aws.Route53;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// AWS Route53 implementation of IDnsRegistration.
/// Represents a DNS record registered in a Route53 hosted zone.
/// </summary>
public class AwsDnsRegistration : IDnsRegistration
{
    private readonly Record _record;

    public AwsDnsRegistration(
        string name,
        IDnsZone zone,
        DnsRecord record,
        int? ttl = null,
        Pulumi.Aws.Provider? provider = null
    )
    {
        Name = name;
        Zone = zone;
        Record = record;
        Ttl = ttl ?? 300;
        Proxied = false; // Not applicable for AWS

        _record = new Record(
            name,
            new RecordArgs
            {
                ZoneId = zone.ZoneId,
                Name = record.Name,
                Type = MapRecordType(record.Type),
                Ttl = Ttl,
                Records = new InputList<string> { record.Value },
            },
            new CustomResourceOptions { Provider = provider }
        );
    }

    public string Name { get; }
    public DnsRecord Record { get; }
    public int? Ttl { get; }
    public bool Proxied { get; }
    public IDnsZone Zone { get; }
    public CloudProvider Provider => CloudProvider.AWS;
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
            _ => throw new ArgumentException($"Unsupported DNS record type: {type}"),
        };
}
