using Pulumi;
namespace PulumiCrossCloud.Core;
/// <summary>
/// Supported DNS record types for cross-cloud compute resources.
/// </summary>
public enum DnsRecordType
{
    CNAME,
    TXT,
    A,
    AAAA,
    MX,
    SRV
}

/// <summary>
/// Represents a DNS record required for domain mapping to a compute resource.
/// </summary>
public record DnsRecord(DnsRecordType Type, string Name, Output<string> Value);

