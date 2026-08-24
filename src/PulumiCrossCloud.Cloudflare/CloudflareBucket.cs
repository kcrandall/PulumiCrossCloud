using System.Text.Json;
using Pulumi;
using Pulumi.Cloudflare;
using Pulumi.Cloudflare.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Cloudflare;

public class CloudflareBucket : IBucket
{
    public string Name { get; }
    public CloudProvider Provider => CloudProvider.CloudFlare;
    public Output<string> Arn => _bucket.Id;
    public bool IsPublic { get; }
    public BucketCdnConfiguration? Cdn { get; }
    public bool VersioningEnabled { get; }
    public string? IndexDocument { get; }
    public IEnumerable<CorsRule> CorsRules { get; }
    public Resource Resource => _bucket;

    private readonly R2Bucket _bucket;
    private readonly string _accountId;
    private readonly Input<string>? _apiToken;
    private readonly Provider? _providerResource;
    private readonly string? _r2AccessKey;
    private readonly Output<string>? _r2SecretKey;
    private readonly List<R2CustomDomain> _customDomains = [];

    public CloudflareBucket(
        string name,
        string accountId,
        bool isPublic = false,
        BucketCdnConfiguration? cdn = null,
        bool versioningEnabled = false,
        IEnumerable<CorsRule>? corsRules = null,
        string? indexDocument = null,
        Provider? provider = null,
        string? zoneId = null,
        string? r2AccessKey = null,
        Output<string>? r2SecretKey = null,
        Input<string>? apiToken = null
    )
    {
        Name = name;
        _accountId = accountId;
        _apiToken = apiToken;
        _providerResource = provider;
        _r2AccessKey = r2AccessKey;
        _r2SecretKey = r2SecretKey;
        IsPublic = isPublic;
        Cdn = cdn;
        VersioningEnabled = versioningEnabled;
        IndexDocument = indexDocument;
        CorsRules = corsRules ?? [];

        if (IsPublic && (Cdn == null || !Cdn.Domains.Any()))
            Log.Warn(
                $"Warning: R2 Bucket {name} is set to Public, but no custom domains were provided in the CDN configuration. R2 buckets are private by default; public access is best enabled via Custom Domains to leverage edge caching."
            );

        var options = provider is not null
            ? new CustomResourceOptions { Provider = provider }
            : null;

        // Create R2 bucket
        _bucket = new R2Bucket(
            name,
            new R2BucketArgs { AccountId = accountId, Name = name },
            options
        );

        // Configure custom domains for edge caching if requested
        if (Cdn is not null && Cdn.Domains.Any())
        {
            foreach (var domain in Cdn.Domains)
            {
                if (string.IsNullOrEmpty(zoneId))
                {
                    Log.Warn(
                        $"Warning: Custom domain {domain} requested for R2 bucket {Name} but no ZoneId was provided. Skipping R2CustomDomain creation."
                    );
                    continue;
                }

                var customDomain = new R2CustomDomain(
                    $"{name}-domain-{domain.Replace('.', '-')}",
                    new R2CustomDomainArgs
                    {
                        AccountId = accountId,
                        BucketName = _bucket.Name,
                        Domain = domain,
                        Enabled = true,
                        ZoneId = zoneId,
                    },
                    new CustomResourceOptions { Provider = provider, DependsOn = [_bucket] }
                );

                _customDomains.Add(customDomain);
            }

            // Configure rewrite rule for IndexDocument if specified
            if (!string.IsNullOrEmpty(IndexDocument) && !string.IsNullOrEmpty(zoneId))
            {
                var hosts = Cdn.Domains.ToList();
                var hostExpression = string.Join(
                    " or ",
                    hosts.Select(h => $"http.host eq \"{h}\"")
                );
                var expression = $"({hostExpression}) and (http.request.uri.path eq \"/\")";

                new Ruleset(
                    $"{name}-rewrite-index",
                    new RulesetArgs
                    {
                        ZoneId = zoneId,
                        Name = $"rewrite-index-{name}",
                        Kind = "zone",
                        Phase = "http_request_transform",
                        Rules = new[]
                        {
                            new RulesetRuleArgs
                            {
                                Action = "rewrite",
                                ActionParameters = new RulesetRuleActionParametersArgs
                                {
                                    Uri = new RulesetRuleActionParametersUriArgs
                                    {
                                        Path = new RulesetRuleActionParametersUriPathArgs
                                        {
                                            Value = $"/{IndexDocument}",
                                        },
                                    },
                                },
                                Expression = expression,
                                Description = $"Rewrite root to {IndexDocument} for {name}",
                                Enabled = true,
                            },
                        },
                    },
                    options
                );
            }
        }

        // Configure CORS if rules are provided
        if (CorsRules.Any())
            Log.Debug(
                $"CORS rules for R2 bucket {Name} are defined but provider support may vary."
            );
    }

    public BucketDnsRecords GetRequiredDnsRecords(IEnumerable<string> hostNames)
    {
        var hostList = hostNames.ToList();
        List<PulumiCrossCloud.Core.DnsRecord> cdnRecords = [];

        return new BucketDnsRecords(cdnRecords, []);
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params BucketPermissions[] permissions)
    {
        // Cloudflare R2 uses API Tokens / S3 Compatibility keys for access,
        // not the same IAM policy structure as AWS/GCP.
        // Public access is managed via R2CustomDomain which enables Cloudflare's edge network.
        return [];
    }

    public Output<string> SyncDirectory(
        string localPath,
        string? keyPrefix = null,
        BucketSyncOptions? options = null
    )
    {
        Output<string> accessKey;
        Output<string> secretKey;

        // 1. Try manual override first (e.g. from stack config)
        if (_r2AccessKey != null)
        {
            accessKey = Output.Create(_r2AccessKey);
            secretKey = _r2SecretKey ?? Output.Create("");
        }
        // 2. Automate creation via Account API (requires "Account API Tokens: Edit")
        else if (_apiToken is not null)
        {
            var creds = new CloudflareR2Credentials(
                $"{Name}-creds",
                _accountId,
                Name,
                _apiToken,
                _providerResource
            );
            accessKey = creds.AccessKeyId;
            secretKey = creds.SecretAccessKey;
        }
        else
            throw new InvalidOperationException(
                "Either R2 Access Keys or a Cloudflare API Token must be provided to sync directories to R2."
            );

        var deployment = new CloudflareBucketDeployment(
            $"{Name}-deploy",
            Name,
            _accountId,
            localPath,
            keyPrefix,
            options,
            new ComponentResourceOptions { Parent = _bucket },
            accessKey,
            secretKey
        );
        return deployment.WebsiteUrl;
    }
}
