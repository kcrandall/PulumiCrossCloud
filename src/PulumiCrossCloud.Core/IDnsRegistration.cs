using Pulumi;

namespace PulumiCrossCloud.Core;

/// <summary>
/// Represents a DNS record registration in a zone.
/// </summary>
public interface IDnsRegistration : ICloudResource
{
    /// <summary>
    /// The DNS record being registered.
    /// </summary>
    DnsRecord Record { get; }

    /// <summary>
    /// TTL for the record in seconds.
    /// </summary>
    int? Ttl { get; }

    /// <summary>
    /// Whether the record is proxied through the provider's CDN (CloudFlare-specific).
    /// </summary>
    bool Proxied { get; }

    /// <summary>
    /// The zone this record belongs to.
    /// </summary>
    IDnsZone Zone { get; }

    /// <summary>
    /// The underlying Pulumi resource for dependency tracking.
    /// </summary>
    Resource Resource { get; }
}
