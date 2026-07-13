using Pulumi;
using Pulumi.Gcp.Storage;
using Pulumi.Gcp.Storage.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpBucket : IBucket
{
    public string Name { get; }
    public CloudProvider Provider => CloudProvider.GCP;
    public Output<string> Arn => _bucket.Name;
    public bool IsPublic { get; }
    public BucketCdnConfiguration? Cdn { get; }
    public bool VersioningEnabled { get; }
    public string? IndexDocument { get; }
    public IEnumerable<CorsRule> CorsRules { get; }
    public Resource Resource => _bucket;

    private readonly Pulumi.Gcp.Storage.Bucket _bucket;
    private Pulumi.Gcp.Storage.BucketIAMMember? _publicReadMember;

    // Optional CDN-related resources
    private Pulumi.Gcp.Compute.BackendBucket? _backendBucket;
    private Pulumi.Gcp.Compute.ManagedSslCertificate? _managedCert;
    private Pulumi.Gcp.Compute.URLMap? _urlMap;
    private Pulumi.Gcp.Compute.TargetHttpsProxy? _httpsProxy;
    private Pulumi.Gcp.Compute.GlobalAddress? _globalAddress;
    private Pulumi.Gcp.Compute.GlobalForwardingRule? _httpsForwardingRule;
    private Pulumi.Gcp.Compute.TargetHttpProxy? _httpProxy;
    private Pulumi.Gcp.Compute.GlobalAddress? _httpGlobalAddress;
    private Pulumi.Gcp.Compute.GlobalForwardingRule? _httpForwardingRule;

    public GcpBucket(
        string name,
        bool isPublic = false,
        BucketCdnConfiguration? cdn = null,
        bool versioningEnabled = false,
        IEnumerable<CorsRule>? corsRules = null,
        string? indexDocument = null,
        Pulumi.Gcp.Provider? provider = null
    )
    {
        Name = name;
        IsPublic = isPublic;
        Cdn = cdn;
        VersioningEnabled = versioningEnabled;
        IndexDocument = indexDocument;
        CorsRules = corsRules ?? [];

        var projectId = GcpProjectConfig.ProjectId;
        var location = GcpProjectConfig.Region;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };

        // Website configuration
        BucketWebsiteArgs? websiteArgs = IndexDocument is not null
            ? new BucketWebsiteArgs { MainPageSuffix = IndexDocument }
            : null;

        // Create GCS bucket with versioning enabled if requested
        var bucketArgs = new Pulumi.Gcp.Storage.BucketArgs
        {
            Project = projectId,
            Name = name,
            Location = location,
            UniformBucketLevelAccess = true,
        };

        if (websiteArgs is not null)
            bucketArgs.Website = websiteArgs;

        // Note: GCP Cloud Storage has versioning enabled by default for new buckets
        // If versioning is explicitly requested, we log it for transparency
        if (VersioningEnabled)
        {
            bucketArgs.Versioning = new BucketVersioningArgs { Enabled = true };
            Log.Debug($"Enabled object versioning for bucket {Name}");
        }

        // Log CORS configuration for manual application
        if (CorsRules.Any())
        {
            // Map your CorsRule -> BucketCorsArgs
            var mapped = CorsRules
                .Select(r => new BucketCorArgs()
                {
                    // AllowedOrigins -> Origins
                    Origins = (r.AllowedOrigins).ToList(),
                    // AllowedMethods -> Methods
                    Methods = (r.AllowedMethods).ToList(),
                    // ExposedHeaders -> ResponseHeaders (GCS exposes, not request-allow list)
                    ResponseHeaders = (r.ExposedHeaders).ToList(),
                    // Max age
                    MaxAgeSeconds = r.MaxAgeSeconds,
                })
                .ToList();

            bucketArgs.Cors = mapped;

            // Optional: log what we’re applying
            Log.Debug($"Applying {mapped.Count} CORS rule(s) to bucket {Name}");
            // If you were setting AllowedHeaders previously: GCS CORS does not support an explicit
            // "allowed request headers" list; it’s ignored here by design.
        }

        _bucket = new Pulumi.Gcp.Storage.Bucket(name, bucketArgs, customResourceOptions);

        // Optional public read access
        if (IsPublic)
        {
            _publicReadMember = new Pulumi.Gcp.Storage.BucketIAMMember(
                $"{name}-public-read",
                new Pulumi.Gcp.Storage.BucketIAMMemberArgs
                {
                    Bucket = _bucket.Name,
                    Role = "roles/storage.objectViewer",
                    Member = "allUsers",
                },
                new CustomResourceOptions { DependsOn = new[] { _bucket }, Provider = provider }
            );
            Log.Warn($"Bucket {Name} being set to public.");
        }

        // Optional CDN in front of the bucket via HTTP(S) LB (BackendBucket)
        if (cdn is not null)
        {
            _backendBucket = new Pulumi.Gcp.Compute.BackendBucket(
                $"{name}-backend-bucket",
                new Pulumi.Gcp.Compute.BackendBucketArgs
                {
                    Project = projectId,
                    BucketName = _bucket.Name,
                    EnableCdn = true,
                },
                customResourceOptions
            );

            _urlMap = new Pulumi.Gcp.Compute.URLMap(
                $"{name}-url-map",
                new Pulumi.Gcp.Compute.URLMapArgs { DefaultService = _backendBucket.SelfLink },
                customResourceOptions
            );

            // If domains are provided, create HTTPS proxy with managed certificate.
            if (cdn.Domains is not null && cdn.Domains.Any())
            {
                _managedCert = new Pulumi.Gcp.Compute.ManagedSslCertificate(
                    $"{name}-managed-cert",
                    new Pulumi.Gcp.Compute.ManagedSslCertificateArgs
                    {
                        Project = projectId,
                        Managed = new Pulumi.Gcp.Compute.Inputs.ManagedSslCertificateManagedArgs
                        {
                            Domains = cdn.Domains.ToArray(),
                        },
                    },
                    customResourceOptions
                );

                _httpsProxy = new Pulumi.Gcp.Compute.TargetHttpsProxy(
                    $"{name}-https-proxy",
                    new Pulumi.Gcp.Compute.TargetHttpsProxyArgs
                    {
                        Project = projectId,
                        SslCertificates = new[] { _managedCert.Name },
                        UrlMap = _urlMap.Name,
                    },
                    customResourceOptions
                );

                _globalAddress = new Pulumi.Gcp.Compute.GlobalAddress(
                    $"{name}-global-ip",
                    new Pulumi.Gcp.Compute.GlobalAddressArgs
                    {
                        Project = projectId,
                        AddressType = "EXTERNAL",
                        IpVersion = "IPV4",
                    },
                    customResourceOptions
                );

                _httpsForwardingRule = new Pulumi.Gcp.Compute.GlobalForwardingRule(
                    $"{name}-https-fr",
                    new Pulumi.Gcp.Compute.GlobalForwardingRuleArgs
                    {
                        Project = projectId,
                        Target = _httpsProxy.SelfLink,
                        IpAddress = _globalAddress.Address,
                        PortRange = "443",
                        LoadBalancingScheme = "EXTERNAL",
                    },
                    customResourceOptions
                );
            }
            else
            {
                // No domains: provide HTTP-only CDN with a global IPv4 address.
                _httpProxy = new Pulumi.Gcp.Compute.TargetHttpProxy(
                    $"{name}-http-proxy",
                    new Pulumi.Gcp.Compute.TargetHttpProxyArgs
                    {
                        Project = projectId,
                        UrlMap = _urlMap.Name,
                    },
                    customResourceOptions
                );

                _httpGlobalAddress = new Pulumi.Gcp.Compute.GlobalAddress(
                    $"{name}-http-global-ip",
                    new Pulumi.Gcp.Compute.GlobalAddressArgs
                    {
                        Project = projectId,
                        AddressType = "EXTERNAL",
                        IpVersion = "IPV4",
                    },
                    customResourceOptions
                );

                _httpForwardingRule = new Pulumi.Gcp.Compute.GlobalForwardingRule(
                    $"{name}-http-fr",
                    new Pulumi.Gcp.Compute.GlobalForwardingRuleArgs
                    {
                        Project = projectId,
                        Target = _httpProxy.SelfLink,
                        IpAddress = _httpGlobalAddress.Address,
                        PortRange = "80",
                        LoadBalancingScheme = "EXTERNAL",
                    },
                    customResourceOptions
                );
            }
        }
    }

    public BucketDnsRecords GetRequiredDnsRecords(IEnumerable<string> hostNames)
    {
        var hostList = hostNames.ToList();

        List<DnsRecord> cdnRecords = [];
        if (Cdn is not null)
        {
            var ipOutput = _globalAddress is not null
                ? _globalAddress.Address
                : _httpGlobalAddress?.Address;
            if (ipOutput is not null)
                foreach (var host in hostList)
                    cdnRecords.Add(new DnsRecord(DnsRecordType.A, host, ipOutput, $"{host}-a"));
        }

        List<DnsRecord> bucketRecords = [];
        // Point to generic GCS endpoint; users can also choose to skip if using CDN exclusively
        foreach (var host in hostList)
            bucketRecords.Add(
                new DnsRecord(
                    DnsRecordType.CNAME,
                    host,
                    Output.Create("storage.googleapis.com"),
                    $"{host}-cname"
                )
            );

        return new BucketDnsRecords(cdnRecords, bucketRecords);
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params BucketPermissions[] permissions)
    {
        List<IPolicyStatement> statements = [];
        foreach (var permission in permissions)
            switch (permission)
            {
                case BucketPermissions.ReadObject:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/storage.objectViewer" },
                            Resources = new[] { Arn },
                        }
                    );
                    break;
                case BucketPermissions.WriteObject:
                case BucketPermissions.DeleteObject:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/storage.objectAdmin" },
                            Resources = new[] { Arn },
                        }
                    );
                    break;
                case BucketPermissions.ListBucket:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/storage.legacyBucketReader" },
                            Resources = new[] { Arn },
                        }
                    );
                    break;
                case BucketPermissions.GetBucketMetadata:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/storage.viewer" },
                            Resources = new[] { Arn },
                        }
                    );
                    break;
                case BucketPermissions.ManageBucket:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/storage.admin" },
                            Resources = new[] { Arn },
                        }
                    );
                    break;
            }

        return statements;
    }

    public Output<string> SyncDirectory(
        string localPath,
        string? keyPrefix = null,
        BucketSyncOptions? options = null
    )
    {
        var deployment = new GcpBucketDeployment(
            $"{Name}-deploy",
            Name,
            localPath,
            keyPrefix,
            options,
            new ComponentResourceOptions { Parent = _bucket }
        );
        return deployment.WebsiteUrl;
    }
}
