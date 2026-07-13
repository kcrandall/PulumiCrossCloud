using System.Collections.Immutable;
using Pulumi;
using Pulumi.Cloudflare;
using Pulumi.Cloudflare.Inputs;
using PulumiCrossCloud.Core;
using CoreDnsRecord = PulumiCrossCloud.Core.DnsRecord;

namespace PulumiCrossCloud.Cloudflare;

/// <summary>
/// CloudFlare implementation of IDnsZone.
/// Represents a CloudFlare DNS zone that can manage DNS records.
/// </summary>
public class CloudflareDnsZone : IDnsZone
{
    private readonly Zone? _zone;
    private readonly Provider? _provider;

    /// <summary>
    /// Creates a new CloudFlare DNS zone.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name for the zone.</param>
    /// <param name="accountId">The CloudFlare account ID.</param>
    /// <param name="provider">Optional custom CloudFlare provider.</param>
    public CloudflareDnsZone(
        string name,
        string domainName,
        string accountId,
        Provider? provider = null
    )
    {
        Name = name;
        DomainName = domainName;
        _provider = provider;

        var options = provider is not null
            ? new CustomResourceOptions { Provider = provider }
            : null;

        _zone = new Zone(
            name,
            new ZoneArgs
            {
                Account = new ZoneAccountArgs { Id = accountId },
                Name = domainName,
                Type = "full",
            },
            options
        );
    }

    /// <summary>
    /// Looks up an existing CloudFlare DNS zone by zone ID.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="zoneId">The CloudFlare zone ID to look up.</param>
    /// <param name="provider">Optional custom CloudFlare provider.</param>
    /// <returns>A CloudflareDnsZoneLookup instance.</returns>
    public static IDnsZone LookupByZoneId(string name, string zoneId, Provider? provider = null)
    {
        // Some provider versions have inconsistent GetZone(Id) behavior. For reliability,
        // bypass the data source and use the known ZoneId directly for record operations.
        return new CloudflareDnsZoneKnownId(name, zoneId, provider);
    }

    /// <summary>
    /// Looks up an existing CloudFlare DNS zone by domain name.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name of the zone to look up.</param>
    /// <param name="accountId">Optional account ID to filter by (ignored if not supported by provider).</param>
    /// <param name="provider">Optional custom CloudFlare provider.</param>
    /// <returns>A CloudflareDnsZoneLookup instance.</returns>
    public static IDnsZone LookupByDomainName(
        string name,
        string domainName,
        string? accountId = null,
        string? apiToken = null,
        Provider? provider = null
    )
    {
        var options = new InvokeOptions { Provider = provider };

        var cleanDomain = domainName.Trim().ToLowerInvariant();

        var filter = new GetZoneFilterInputArgs { Name = cleanDomain, Match = "all" };

        var lookup = GetZone.Invoke(new GetZoneInvokeArgs { Filter = filter }, options);
        return new CloudflareDnsZoneLookupWithFallback(
            name,
            lookup,
            cleanDomain,
            apiToken,
            provider
        );
    }

    public string Name { get; }
    public string DomainName { get; }
    public CloudProvider Provider => CloudProvider.CloudFlare;

    public Output<string> Arn => ZoneId;

    public Output<string> ZoneId =>
        _zone?.Id
        ?? throw new InvalidOperationException(
            "Zone resource is not available. Ensure the zone was created or looked up successfully."
        );

    public Output<ImmutableArray<string>> NameServers =>
        _zone?.NameServers.Apply(ns => ns.ToImmutableArray())
        ?? Output.Create(ImmutableArray<string>.Empty);

    public Resource Resource =>
        _zone ?? throw new InvalidOperationException("Zone resource is not available");

    public IDnsRegistration RegisterRecord(
        CoreDnsRecord record,
        int? ttl = null,
        bool proxied = false
    )
    {
        var suffix = record.Key.Replace(".", "-").ToLowerInvariant();
        var resourceName = $"{Name}-{record.Type.ToString().ToLowerInvariant()}-{suffix}";
        return new CloudflareDnsRegistration(resourceName, this, record, ttl, proxied, _provider);
    }

    public IEnumerable<IDnsRegistration> RegisterRecords(
        IEnumerable<CoreDnsRecord> records,
        int? ttl = null,
        bool proxied = false
    )
    {
        return records.Select(r => RegisterRecord(r, ttl, proxied)).ToList();
    }
}

/// <summary>
/// CloudFlare DNS zone created from a lookup operation.
/// </summary>
internal class CloudflareDnsZoneLookup : IDnsZone
{
    private readonly Output<GetZoneResult> _lookupResult;
    private readonly Provider? _provider;
    private readonly string _searchedDomain;

    public CloudflareDnsZoneLookup(
        string name,
        Output<GetZoneResult> lookupResult,
        string searchedDomain,
        Provider? provider = null
    )
    {
        Name = name;
        _lookupResult = lookupResult;
        _searchedDomain = searchedDomain;
        _provider = provider;
    }

    public string Name { get; }
    public string DomainName => _searchedDomain;
    public CloudProvider Provider => CloudProvider.CloudFlare;

    public Output<string> Arn => ZoneId;

    public Output<string> ZoneId =>
        _lookupResult.Apply(r =>
        {
            // Some provider versions expose the zone ID as ZoneId instead of Id.
            var id = r.Id;
#pragma warning disable CS8073
            if (string.IsNullOrEmpty(id))
            {
                try
                {
                    // Use reflection to avoid compile-time dependency on a specific member name.
                    var zoneIdProp = r.GetType().GetProperty("ZoneId");
                    if (zoneIdProp is not null)
                    {
                        var alt = zoneIdProp.GetValue(r) as string;
                        if (!string.IsNullOrEmpty(alt))
                            id = alt;
                    }
                }
                catch
                {
                    // ignore, we'll fall through to error handling below
                }
            }
#pragma warning restore CS8073

            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException(
                    $"CloudFlare zone lookup failed for domain '{_searchedDomain}': zone ID is empty. "
                        + "Possible causes:\n"
                        + $"1. The zone '{_searchedDomain}' does not exist in your Cloudflare account\n"
                        + "2. The API token lacks permissions to access this zone\n"
                        + "3. The zone exists but the filter did not match (check the exact domain name)\n"
                        + $"Returned Name: '{r.Name}', Returned Id fields appear empty.\n"
                        + $"Provider: {(_provider != null ? "Custom" : "Default")}"
                );

            return id;
        });

    public Output<ImmutableArray<string>> NameServers =>
        _lookupResult.Apply(r => r.NameServers.ToImmutableArray());

    public Resource Resource =>
        throw new InvalidOperationException(
            "Cannot get underlying resource from a lookup-based zone. "
                + "Use CreateZone instead of LookupZone if you need the resource for dependencies."
        );

    public IDnsRegistration RegisterRecord(
        CoreDnsRecord record,
        int? ttl = null,
        bool proxied = false
    )
    {
        var suffix = record.Key.Replace(".", "-").ToLowerInvariant();
        var resourceName = $"{Name}-{record.Type.ToString().ToLowerInvariant()}-{suffix}";
        return new CloudflareDnsRegistration(resourceName, this, record, ttl, proxied, _provider);
    }

    public IEnumerable<IDnsRegistration> RegisterRecords(
        IEnumerable<CoreDnsRecord> records,
        int? ttl = null,
        bool proxied = false
    )
    {
        return records.Select(r => RegisterRecord(r, ttl, proxied)).ToList();
    }
}

/// <summary>
/// Lookup-based zone with REST fallback when provider returns empty ID.
/// </summary>
internal class CloudflareDnsZoneLookupWithFallback : IDnsZone
{
    private readonly Output<GetZoneResult> _lookupResult;
    private readonly Provider? _provider;
    private readonly string _searchedDomain;
    private readonly string? _apiToken;

    public CloudflareDnsZoneLookupWithFallback(
        string name,
        Output<GetZoneResult> lookupResult,
        string searchedDomain,
        string? apiToken,
        Provider? provider = null
    )
    {
        Name = name;
        _lookupResult = lookupResult;
        _searchedDomain = searchedDomain;
        _apiToken = apiToken;
        _provider = provider;
    }

    public string Name { get; }
    public string DomainName => _searchedDomain;
    public CloudProvider Provider => CloudProvider.CloudFlare;
    public Output<string> Arn => ZoneId;

    public Output<string> ZoneId =>
        _lookupResult.Apply(r =>
        {
            var id = r.Id;
            if (string.IsNullOrEmpty(id))
            {
                try
                {
                    var zoneIdProp = r.GetType().GetProperty("ZoneId");
                    if (zoneIdProp is not null)
                    {
                        var alt = zoneIdProp.GetValue(r) as string;
                        if (!string.IsNullOrEmpty(alt))
                            id = alt;
                    }
                }
                catch
                { /* ignore */
                }
            }

            if (!string.IsNullOrEmpty(id))
                return Output.Create(id);

            if (string.IsNullOrEmpty(_apiToken))
                throw new InvalidOperationException(
                    $"CloudFlare zone lookup failed for domain '{_searchedDomain}': zone ID is empty and no API token provided for REST fallback."
                );

            return Output.Create(
                Task.Run(async () =>
                {
                    using var http = new System.Net.Http.HttpClient();
                    var url =
                        $"https://api.cloudflare.com/client/v4/zones?name={Uri.EscapeDataString(_searchedDomain)}";
                    var req = new System.Net.Http.HttpRequestMessage(
                        System.Net.Http.HttpMethod.Get,
                        url
                    );
                    req.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiToken);
                    var resp = await http.SendAsync(req).ConfigureAwait(false);
                    var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException(
                            $"Cloudflare REST lookup failed: {(int)resp.StatusCode} {resp.ReasonPhrase} - {json}"
                        );

                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (
                            root.TryGetProperty("result", out var result)
                            && result.GetArrayLength() > 0
                        )
                        {
                            var first = result[0];
                            if (first.TryGetProperty("id", out var idProp))
                            {
                                var restId = idProp.GetString();
                                if (!string.IsNullOrEmpty(restId))
                                    return restId!;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to parse Cloudflare REST lookup response: {ex.Message}\nBody: {json}"
                        );
                    }
                    throw new InvalidOperationException(
                        $"Cloudflare REST lookup returned no zone for '{_searchedDomain}'. Body: {json}"
                    );
                })
            );
        });

    public Output<System.Collections.Immutable.ImmutableArray<string>> NameServers =>
        _lookupResult.Apply(r => r.NameServers.ToImmutableArray());

    public Resource Resource =>
        throw new InvalidOperationException(
            "Cannot get underlying resource from a lookup-based zone. "
                + "Use CreateZone instead of LookupZone if you need the resource for dependencies."
        );

    public IDnsRegistration RegisterRecord(
        CoreDnsRecord record,
        int? ttl = null,
        bool proxied = false
    )
    {
        var suffix = record.Key.Replace(".", "-").ToLowerInvariant();
        var resourceName = $"{Name}-{record.Type.ToString().ToLowerInvariant()}-{suffix}";
        return new CloudflareDnsRegistration(resourceName, this, record, ttl, proxied, _provider);
    }

    public IEnumerable<IDnsRegistration> RegisterRecords(
        IEnumerable<CoreDnsRecord> records,
        int? ttl = null,
        bool proxied = false
    )
    {
        return records.Select(r => RegisterRecord(r, ttl, proxied)).ToList();
    }
}

/// <summary>
/// CloudFlare DNS zone wrapper when the Zone ID is already known.
/// Avoids provider data-source quirks by supplying the ID directly.
/// </summary>
internal class CloudflareDnsZoneKnownId : IDnsZone
{
    private readonly string _zoneId;
    private readonly Provider? _provider;

    public CloudflareDnsZoneKnownId(string name, string zoneId, Provider? provider = null)
    {
        Name = name;
        _zoneId = zoneId;
        _provider = provider;
    }

    public string Name { get; }
    public string DomainName => string.Empty; // Unknown without lookup
    public CloudProvider Provider => CloudProvider.CloudFlare;

    public Output<string> Arn => ZoneId;
    public Output<string> ZoneId => Output.Create(_zoneId);
    public Output<System.Collections.Immutable.ImmutableArray<string>> NameServers =>
        Output.Create(System.Collections.Immutable.ImmutableArray<string>.Empty);

    public Resource Resource =>
        throw new InvalidOperationException(
            "Cannot get underlying resource from a known-id zone wrapper. "
                + "Use CreateZone if you need a resource instance."
        );

    public IDnsRegistration RegisterRecord(
        CoreDnsRecord record,
        int? ttl = null,
        bool proxied = false
    )
    {
        var suffix = record.Key.Replace(".", "-").ToLowerInvariant();
        var resourceName = $"{Name}-{record.Type.ToString().ToLowerInvariant()}-{suffix}";
        return new CloudflareDnsRegistration(resourceName, this, record, ttl, proxied, _provider);
    }

    public IEnumerable<IDnsRegistration> RegisterRecords(
        IEnumerable<CoreDnsRecord> records,
        int? ttl = null,
        bool proxied = false
    ) => records.Select(r => RegisterRecord(r, ttl, proxied)).ToList();
}
