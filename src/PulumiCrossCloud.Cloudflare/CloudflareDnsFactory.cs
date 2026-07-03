using Pulumi.Cloudflare;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Cloudflare;

/// <summary>
/// Factory for creating CloudFlare DNS resources.
/// Since CloudFlare is primarily a DNS/CDN provider rather than a general cloud provider,
/// it has a dedicated factory separate from CloudResourceFactory.
/// </summary>
public class CloudflareDnsFactory
{
    private readonly string? _defaultAccountId;
    private readonly Provider? _provider;

    /// <summary>
    /// Creates a new CloudflareDnsFactory.
    /// </summary>
    /// <param name="defaultAccountId">Optional default CloudFlare account ID to use when creating zones.</param>
    /// <param name="provider">Optional custom CloudFlare provider for multi-account scenarios.</param>
    public CloudflareDnsFactory(string? defaultAccountId = null, Provider? provider = null)
    {
        _defaultAccountId = defaultAccountId;
        _provider = provider;
    }

    /// <summary>
    /// Creates a new CloudFlare DNS zone.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name for the zone (e.g., "example.com").</param>
    /// <param name="accountId">The CloudFlare account ID. If not provided, uses the default account ID.</param>
    /// <returns>An IDnsZone instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no account ID is provided or configured.</exception>
    public IDnsZone CreateZone(string name, string domainName, string? accountId = null)
    {
        var effectiveAccountId =
            accountId
            ?? _defaultAccountId
            ?? throw new InvalidOperationException(
                "CloudFlare account ID is required. Provide it via the accountId parameter or set a default in the factory constructor."
            );

        return new CloudflareDnsZone(name, domainName, effectiveAccountId, _provider);
    }

    /// <summary>
    /// Looks up an existing CloudFlare DNS zone by domain name.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name of the zone to look up.</param>
    /// <returns>An IDnsZone instance.</returns>
    public IDnsZone LookupZone(string name, string domainName)
    {
        return CloudflareDnsZone.LookupByDomainName(name, domainName, _defaultAccountId, apiToken: null, provider: _provider);
    }

    /// <summary>
    /// Looks up an existing CloudFlare DNS zone by zone ID.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="zoneId">The CloudFlare zone ID.</param>
    /// <returns>An IDnsZone instance.</returns>
    public IDnsZone LookupZoneById(string name, string zoneId)
    {
        return CloudflareDnsZone.LookupByZoneId(name, zoneId, _provider);
    }
}
