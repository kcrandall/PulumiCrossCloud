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
    SRV,
    NS,
    CAA,
    PTR
}

/// <summary>
/// Represents a DNS record required for domain mapping to a compute resource.
/// </summary>
/// <param name="Type">The DNS record type.</param>
/// <param name="Name">The record name (subdomain or full name).</param>
/// <param name="Value">The record value.</param>
/// <param name="Key">Required key for deterministic resource naming (e.g. "spf", "mx-1"). Must be unique within the zone context.</param>
public record DnsRecord(DnsRecordType Type, Output<string> Name, Output<string> Value, string Key)
{
    /// <summary>
    /// Helper constructor for string inputs.
    /// </summary>
    public DnsRecord(DnsRecordType Type, string Name, Output<string> Value, string Key) 
        : this(Type, Output.Create(Name), Value, Key) { }

    /// <summary>
    /// Helper constructor for string inputs.
    /// </summary>
    public DnsRecord(DnsRecordType Type, string Name, string Value, string Key) 
        : this(Type, Output.Create(Name), Output.Create(Value), Key) { }
}
