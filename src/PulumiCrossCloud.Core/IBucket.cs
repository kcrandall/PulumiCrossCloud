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
    /// The default index document to serve (e.g., "index.html").
    /// </summary>
    string? IndexDocument { get; }

    /// <summary>
    /// CORS rules configured for the bucket.
    /// </summary>
    IEnumerable<CorsRule> CorsRules { get; }

    /// <summary>
    /// Returns the DNS records to configure for the bucket and the CDN if configured.
    /// </summary>
    BucketDnsRecords GetRequiredDnsRecords(IEnumerable<string> hostNames);

    /// <summary>
    /// Synchronizes a local directory to the bucket.
    /// This is an efficient, idempotent operation that only uploads changes.
    /// </summary>
    /// <param name="localPath">The local filesystem path to sync (the output directory).</param>
    /// <param name="keyPrefix">Optional prefix (folder) in the bucket.</param>
    /// <param name="options">Optional build and sync configuration.</param>
    /// <returns>The URL or path where the site is deployed.</returns>
    Output<string> SyncDirectory(string localPath, string? keyPrefix = null, BucketSyncOptions? options = null);

    /// <summary>
    /// Exposes the underlying bucket resource.
    /// </summary>
    Resource Resource { get; }
}

/// <summary>
/// Configuration for bucket synchronization, including build steps.
/// </summary>
public record BucketSyncOptions
{
    /// <summary>
    /// Optional command to run before syncing (e.g. "npm run build").
    /// </summary>
    public string? BuildCommand { get; init; }

    /// <summary>
    /// The working directory for the build command.
    /// </summary>
    public string? BuildCwd { get; init; }

    /// <summary>
    /// The directory to hash to decide if build/sync is needed. 
    /// If null, the localPath (output directory) is hashed instead.
    /// Use this if your build command is triggered by source files.
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Optional Access Key ID for Cloudflare R2 (S3-compatible).
    /// </summary>
    public string? R2AccessKeyId { get; init; }

    /// <summary>
    /// Optional Secret Access Key for Cloudflare R2 (S3-compatible).
    /// </summary>
    public Output<string>? R2SecretAccessKey { get; init; }
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
