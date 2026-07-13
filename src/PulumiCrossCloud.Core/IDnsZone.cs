using System.Collections.Immutable;
using Pulumi;

namespace PulumiCrossCloud.Core;

/// <summary>
/// Represents a DNS zone that can manage DNS records.
/// </summary>
public interface IDnsZone : ICloudResource
{
    /// <summary>
    /// The domain name of the zone (e.g., "example.com").
    /// </summary>
    string DomainName { get; }

    /// <summary>
    /// The zone identifier (provider-specific ID).
    /// </summary>
    Output<string> ZoneId { get; }

    /// <summary>
    /// Name servers for this zone (if applicable).
    /// </summary>
    Output<ImmutableArray<string>> NameServers { get; }

    /// <summary>
    /// Register a DNS record in this zone.
    /// </summary>
    /// <param name="record">The DNS record to register.</param>
    /// <param name="ttl">TTL in seconds. If null, provider default is used.</param>
    /// <param name="proxied">Whether to proxy the record through the provider's CDN (CloudFlare-specific, ignored by other providers).</param>
    /// <returns>The DNS registration resource.</returns>
    IDnsRegistration RegisterRecord(DnsRecord record, int? ttl = null, bool proxied = false);

    /// <summary>
    /// Register multiple DNS records in this zone.
    /// </summary>
    /// <param name="records">The DNS records to register.</param>
    /// <param name="ttl">TTL in seconds. If null, provider default is used.</param>
    /// <param name="proxied">Whether to proxy the records through the provider's CDN (CloudFlare-specific, ignored by other providers).</param>
    /// <returns>The DNS registration resources.</returns>
    IEnumerable<IDnsRegistration> RegisterRecords(
        IEnumerable<DnsRecord> records,
        int? ttl = null,
        bool proxied = false
    );

    /// <summary>
    /// The underlying Pulumi resource for dependency tracking.
    /// </summary>
    Resource Resource { get; }
}
