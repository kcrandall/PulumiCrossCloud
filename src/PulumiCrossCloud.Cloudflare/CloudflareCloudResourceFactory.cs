using Pulumi;
using Pulumi.Cloudflare;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Cloudflare;

/// <summary>
/// CloudFlare implementation of CloudResourceFactory.
/// CloudFlare is primarily a DNS/CDN provider, so only DNS-related methods are implemented.
/// Other methods (CreateSecret, CreateCompute, etc.) will throw NotSupportedException from the base class.
/// </summary>
public class CloudflareCloudResourceFactory : CloudResourceFactory
{
    private readonly string? _accountId;
    private readonly Provider? _cloudflareProvider;
    private readonly string? _rawApiToken;
    private readonly Input<string>? _apiToken;

    /// <summary>
    /// Creates a new CloudflareCloudResourceFactory with an account ID and an API token.
    /// </summary>
    /// <param name="accountId">CloudFlare account ID required for creating new zones.</param>
    /// <param name="apiToken">Cloudflare API token.</param>
    public CloudflareCloudResourceFactory(string accountId, string apiToken)
        : base(CloudProvider.CloudFlare)
    {
        _accountId = accountId;
        _rawApiToken = apiToken;
        _apiToken = apiToken;
        _cloudflareProvider = new Provider(
            "cloudflare-provider",
            new ProviderArgs { ApiToken = apiToken }
        );
    }

    /// <summary>
    /// Creates a new CloudflareCloudResourceFactory with an account ID and an API token.
    /// </summary>
    /// <param name="accountId">CloudFlare account ID required for creating new zones.</param>
    /// <param name="apiToken">Cloudflare API token.</param>
    public CloudflareCloudResourceFactory(string accountId, Input<string> apiToken)
        : base(CloudProvider.CloudFlare)
    {
        _accountId = accountId;
        _apiToken = apiToken;
        _cloudflareProvider = new Provider(
            "cloudflare-provider",
            new ProviderArgs { ApiToken = apiToken }
        );
    }

    /// <summary>
    /// Creates a new CloudflareCloudResourceFactory with an API token.
    /// </summary>
    /// <param name="apiToken">Cloudflare API token.</param>
    public CloudflareCloudResourceFactory(string apiToken)
        : base(CloudProvider.CloudFlare)
    {
        _rawApiToken = apiToken;
        _apiToken = apiToken;
        _cloudflareProvider = new Provider(
            "cloudflare-provider",
            new ProviderArgs { ApiToken = apiToken }
        );
    }

    /// <summary>
    /// Creates a new CloudflareCloudResourceFactory with an API token.
    /// </summary>
    /// <param name="apiToken">Cloudflare API token.</param>
    public CloudflareCloudResourceFactory(Input<string> apiToken)
        : base(CloudProvider.CloudFlare)
    {
        _apiToken = apiToken;
        _cloudflareProvider = new Provider(
            "cloudflare-provider",
            new ProviderArgs { ApiToken = apiToken }
        );
    }

    /// <summary>
    /// Creates a new CloudflareCloudResourceFactory.
    /// </summary>
    /// <param name="provider">Optional custom CloudFlare provider for multi-account scenarios.</param>
    public CloudflareCloudResourceFactory(Provider? provider = null)
        : base(CloudProvider.CloudFlare)
    {
        _cloudflareProvider = provider;
    }

    /// <summary>
    /// Creates a new CloudflareCloudResourceFactory with an account ID for zone creation.
    /// </summary>
    /// <param name="accountId">CloudFlare account ID required for creating new zones.</param>
    /// <param name="provider">Optional custom CloudFlare provider for multi-account scenarios.</param>
    public CloudflareCloudResourceFactory(string accountId, Provider? provider = null)
        : base(CloudProvider.CloudFlare)
    {
        _accountId = accountId;
        _cloudflareProvider = provider;
    }

    /// <summary>
    /// Creates a new CloudFlare DNS zone.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name for the zone (e.g., "example.com").</param>
    /// <returns>An IDnsZone instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no account ID is configured.</exception>
    public override IDnsZone CreateDnsZone(string name, string domainName)
    {
        var accountId =
            _accountId
            ?? throw new InvalidOperationException(
                "CloudFlare account ID is required to create zones. Use the constructor overload that accepts an accountId."
            );

        return new CloudflareDnsZone(name, domainName, accountId, _cloudflareProvider);
    }

    /// <summary>
    /// Looks up an existing CloudFlare DNS zone by domain name.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name of the zone to look up.</param>
    /// <returns>An IDnsZone instance.</returns>
    public override IDnsZone LookupDnsZone(string name, string domainName)
    {
        return CloudflareDnsZone.LookupByDomainName(
            name,
            domainName,
            _accountId,
            _rawApiToken,
            _cloudflareProvider
        );
    }

    /// <summary>
    /// Looks up an existing CloudFlare DNS zone by zone ID.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="zoneId">The CloudFlare zone ID.</param>
    /// <returns>An IDnsZone instance.</returns>
    public IDnsZone LookupDnsZoneById(string name, string zoneId)
    {
        return CloudflareDnsZone.LookupByZoneId(name, zoneId, _cloudflareProvider);
    }

    /// <summary>
    /// Creates a new CloudFlare R2 bucket.
    /// </summary>
    public override IBucket CreateBucket(
        string name,
        bool isPublic = false,
        BucketCdnConfiguration? cdn = null,
        bool versioningEnabled = false,
        IEnumerable<CorsRule>? corsRules = null,
        string? indexDocument = null
    )
    {
        var accountId =
            _accountId
            ?? (
                string.IsNullOrEmpty(CloudflareProjectConfig.AccountId)
                    ? throw new InvalidOperationException(
                        "CloudFlare account ID is required to create buckets."
                    )
                    : CloudflareProjectConfig.AccountId
            );

        return new CloudflareBucket(
            name,
            accountId,
            isPublic,
            cdn,
            versioningEnabled,
            corsRules,
            indexDocument,
            _cloudflareProvider,
            zoneId: CloudflareProjectConfig.ZoneId,
            apiToken: _apiToken
        );
    }

    /// <summary>
    /// Creates or ensures a Cloudflare Advanced Certificate Pack for the provided hosts.
    /// </summary>
    public Pulumi.Resource EnsureEdgeCertificate(
        string name,
        IDnsZone zone,
        IEnumerable<string> hosts
    )
    {
        var options = _cloudflareProvider is not null
            ? new CustomResourceOptions { Provider = _cloudflareProvider }
            : null;

        var hostInputs = new Pulumi.InputList<string>();
        foreach (var h in hosts)
            hostInputs.Add(h);

        var pack = new CertificatePack(
            name,
            new CertificatePackArgs
            {
                ZoneId = zone.ZoneId,
                Type = "advanced",
                Hosts = hostInputs,
                // Required by provider: choose a CA and validation method for Advanced certs
                CertificateAuthority = "google",
                ValidationMethod = "txt",
                ValidityDays = 365,
            },
            options
        );

        return pack;
    }
}
