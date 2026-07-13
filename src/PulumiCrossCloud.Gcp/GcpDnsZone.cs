using System.Collections.Immutable;
using Pulumi;
using Pulumi.Gcp.Dns;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

/// <summary>
/// GCP Cloud DNS implementation of IDnsZone.
/// Represents a Cloud DNS managed zone that can manage DNS records.
/// </summary>
public class GcpDnsZone : IDnsZone
{
    private readonly ManagedZone? _zone;
    private readonly Output<GetManagedZoneResult>? _lookupResult;

    /// <summary>
    /// Creates a new Cloud DNS managed zone.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name for the zone (e.g., "example.com").</param>
    public GcpDnsZone(string name, string domainName, Pulumi.Gcp.Provider? provider = null)
    {
        Name = name;
        DomainName = domainName;
        _provider = provider;

        // GCP requires DNS name to end with a period
        var dnsName = domainName.EndsWith(".") ? domainName : $"{domainName}.";

        _zone = new ManagedZone(
            name,
            new ManagedZoneArgs
            {
                Name = SanitizeZoneName(name),
                DnsName = dnsName,
                Description = $"Managed by PulumiCrossCloud - {name}",
            },
            new CustomResourceOptions { Provider = provider }
        );
    }

    /// <summary>
    /// Creates a GcpDnsZone from an existing managed zone lookup.
    /// </summary>
    private GcpDnsZone(
        string name,
        string domainName,
        Output<GetManagedZoneResult> lookupResult,
        Pulumi.Gcp.Provider? provider = null
    )
    {
        Name = name;
        DomainName = domainName;
        _lookupResult = lookupResult;
        _provider = provider;
    }

    private readonly Pulumi.Gcp.Provider? _provider;

    /// <summary>
    /// Looks up an existing Cloud DNS managed zone by name.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="zoneName">The name of the managed zone in GCP (not the domain name).</param>
    /// <param name="provider">Optional explicit provider.</param>
    /// <returns>A GcpDnsZone instance.</returns>
    public static GcpDnsZone Lookup(
        string name,
        string zoneName,
        Pulumi.Gcp.Provider? provider = null
    )
    {
        var lookup = GetManagedZone.Invoke(
            new GetManagedZoneInvokeArgs { Name = zoneName },
            new InvokeOptions { Provider = provider }
        );
        var domainName = lookup.Apply(r => r.DnsName.TrimEnd('.'));
        return new GcpDnsZone(name, domainName.Apply(d => d), lookup, provider);
    }

    /// <summary>
    /// Creates a GcpDnsZone from an existing managed zone lookup.
    /// </summary>
    private GcpDnsZone(
        string name,
        Output<string> domainName,
        Output<GetManagedZoneResult> lookupResult,
        Pulumi.Gcp.Provider? provider = null
    )
    {
        Name = name;
        _domainNameOutput = domainName;
        DomainName = string.Empty; // Will be resolved from output
        _lookupResult = lookupResult;
        _provider = provider;
    }

    private readonly Output<string>? _domainNameOutput;

    public string Name { get; }
    public string DomainName { get; }
    public CloudProvider Provider => CloudProvider.GCP;

    public Output<string> Arn =>
        _lookupResult is not null
            ? _lookupResult.Apply(r => r.Id)
            : _zone?.Id ?? Output.Create(string.Empty);

    public Output<string> ZoneId =>
        _lookupResult is not null
            ? _lookupResult.Apply(r => r.Name)
            : _zone?.Name ?? Output.Create(string.Empty);

    public Output<ImmutableArray<string>> NameServers =>
        _lookupResult is not null
            ? _lookupResult.Apply(r => r.NameServers.ToImmutableArray())
            : _zone?.NameServers.Apply(ns => ns.ToImmutableArray())
                ?? Output.Create(ImmutableArray<string>.Empty);

    public Resource Resource =>
        _zone
        ?? throw new InvalidOperationException(
            "Cannot get underlying resource from a lookup-based zone. "
                + "Use CreateDnsZone instead of LookupDnsZone if you need the resource for dependencies."
        );

    public IDnsRegistration RegisterRecord(DnsRecord record, int? ttl = null, bool proxied = false)
    {
        // Deterministic naming strategy:
        // {ZoneName}-{RecordType}-{Key}
        // Replace dots with dashes in the key to ensure valid resource name segments (if needed)

        var suffix = record.Key.Replace(".", "-").ToLowerInvariant();
        var resourceName = $"{Name}-{record.Type.ToString().ToLowerInvariant()}-{suffix}";
        return new GcpDnsRegistration(resourceName, this, record, ttl);
    }

    public IEnumerable<IDnsRegistration> RegisterRecords(
        IEnumerable<DnsRecord> records,
        int? ttl = null,
        bool proxied = false
    )
    {
        // proxied is ignored for GCP (CloudFlare-specific)
        return records.Select(r => RegisterRecord(r, ttl, false)).ToList();
    }

    /// <summary>
    /// Sanitizes the zone name for GCP requirements (lowercase, alphanumeric, hyphens only).
    /// </summary>
    private static string SanitizeZoneName(string name) =>
        new string(
            name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()
        );
}
