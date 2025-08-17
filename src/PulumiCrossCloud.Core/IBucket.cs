using Pulumi;

namespace PulumiCrossCloud.Core;

/// <summary>
/// Represents a cloud object storage bucket with optional CDN fronting.
/// </summary>
public interface IBucket : ICloudResource, IPolicyProvider<BucketPermissions>
{
    /// <summary>
    /// Whether the bucket/object access is public (read) via HTTP.
    /// </summary>
    bool IsPublic { get; }

    /// <summary>
    /// Optional CDN configuration if the bucket is fronted by a CDN / HTTP(S) load balancer.
    /// </summary>
    BucketCdnConfiguration? Cdn { get; }

    /// <summary>
    /// Whether versioning is enabled for the bucket.
    /// </summary>
    bool VersioningEnabled { get; }

    /// <summary>
    /// CORS rules configured for the bucket.
    /// </summary>
    IEnumerable<CorsRule> CorsRules { get; }

    /// <summary>
    /// Returns the DNS records to configure for the bucket and the CDN if configured.
    /// </summary>
    BucketDnsRecords GetRequiredDnsRecords(IEnumerable<string> hostNames);

    /// <summary>
    /// Exposes the underlying bucket resource.
    /// </summary>
    Resource Resource { get; }
}

/// <summary>
/// Optional CDN configuration for a bucket.
/// </summary>
public record BucketCdnConfiguration(
    IEnumerable<string> Domains,
    string? CertificateManagedDomain = null,
    bool EnableHttpsRedirect = true
);

/// <summary>
/// DNS records grouped for a bucket: CDN-facing and direct bucket addressing.
/// </summary>
public record BucketDnsRecords(
    IReadOnlyList<DnsRecord> CdnRecords,
    IReadOnlyList<DnsRecord> BucketRecords
);
