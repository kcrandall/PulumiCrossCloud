using System.Collections.Immutable;
using Pulumi;
using Pulumi.Aws.Route53;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// AWS Route53 implementation of IDnsZone.
/// Represents a Route53 hosted zone that can manage DNS records.
/// </summary>
public class AwsDnsZone : IDnsZone
{
    private readonly Zone? _zone;
    private int _recordCounter;

    /// <summary>
    /// Creates a new Route53 hosted zone.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name for the zone.</param>
    public AwsDnsZone(string name, string domainName, Pulumi.Aws.Provider? provider = null)
    {
        Name = name;
        DomainName = domainName;
        _recordCounter = 0;
        _provider = provider;

        _zone = new Zone(
            name,
            new ZoneArgs { Name = domainName, Comment = $"Managed by PulumiCrossCloud - {name}" },
            new CustomResourceOptions { Provider = provider }
        );
    }

    /// <summary>
    /// Creates an AwsDnsZone from an existing hosted zone lookup.
    /// </summary>
    private AwsDnsZone(string name, string domainName, Output<GetZoneResult> lookupResult, Pulumi.Aws.Provider? provider = null)
    {
        Name = name;
        DomainName = domainName;
        _lookupResult = lookupResult;
        _recordCounter = 0;
        _provider = provider;
    }

    private readonly Output<GetZoneResult>? _lookupResult;
    private readonly Pulumi.Aws.Provider? _provider;

    /// <summary>
    /// Looks up an existing Route53 hosted zone by domain name.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name of the zone to look up.</param>
    /// <param name="provider">Optional explicit provider.</param>
    /// <returns>An AwsDnsZone instance.</returns>
    public static AwsDnsZone Lookup(string name, string domainName, Pulumi.Aws.Provider? provider = null)
    {
        var lookup = GetZone.Invoke(new GetZoneInvokeArgs { Name = domainName }, new InvokeOptions { Provider = provider });
        return new AwsDnsZone(name, domainName, lookup, provider);
    }

    public string Name { get; }
    public string DomainName { get; }
    public CloudProvider Provider => CloudProvider.AWS;

    public Output<string> Arn =>
        _lookupResult is not null ? _lookupResult.Apply(r => r.Arn) : _zone?.Arn ?? Output.Create(string.Empty);

    public Output<string> ZoneId =>
        _lookupResult is not null ? _lookupResult.Apply(r => r.ZoneId) : _zone?.ZoneId ?? Output.Create(string.Empty);

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
        var recordName = $"{Name}-record-{_recordCounter++}";
        return new AwsDnsRegistration(recordName, this, record, ttl);
    }

    public IEnumerable<IDnsRegistration> RegisterRecords(
        IEnumerable<DnsRecord> records,
        int? ttl = null,
        bool proxied = false
    )
    {
        // proxied is ignored for AWS (CloudFlare-specific)
        return records.Select(r => RegisterRecord(r, ttl, false)).ToList();
    }
}
