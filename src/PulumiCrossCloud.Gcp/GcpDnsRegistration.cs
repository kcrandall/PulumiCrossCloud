using Pulumi;
using Pulumi.Gcp.Dns;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

/// <summary>
/// GCP Cloud DNS implementation of IDnsRegistration.
/// Represents a DNS record set registered in a Cloud DNS managed zone.
/// </summary>
public class GcpDnsRegistration : IDnsRegistration
{
    private readonly RecordSet _recordSet;

    public GcpDnsRegistration(string name, IDnsZone zone, DnsRecord record, int? ttl = null)
    {
        Name = name;
        Zone = zone;
        Record = record;
        Ttl = ttl ?? 300;
        Proxied = false; // Not applicable for GCP

        // GCP requires record names to be fully qualified (end with the zone's DNS name)
        // Pulumi GCP provider often handles this, or we rely on the input being correct.

        _recordSet = new RecordSet(
            name,
            new RecordSetArgs
            {
                ManagedZone = zone.ZoneId,
                Name = record.Name,
                Type = MapRecordType(record.Type),
                Ttl = Ttl,
                Rrdatas = new InputList<string> { record.Value },
            }
        );
    }

    public string Name { get; }
    public DnsRecord Record { get; }
    public int? Ttl { get; }
    public bool Proxied { get; }
    public IDnsZone Zone { get; }
    public CloudProvider Provider => CloudProvider.GCP;
    public Output<string> Arn => _recordSet.Id;
    public Resource Resource => _recordSet;

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
