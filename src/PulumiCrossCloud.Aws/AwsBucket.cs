using System.Text.Json;
using Pulumi;
using Pulumi.Aws.CloudFront;
using Pulumi.Aws.CloudFront.Inputs;
using Pulumi.Aws.S3;
using Pulumi.Aws.S3.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsBucket : IBucket
{
    public string Name { get; }
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> Arn => _bucket.Arn;
    public bool IsPublic { get; }
    public BucketCdnConfiguration? Cdn { get; }
    public bool VersioningEnabled { get; }
    public string? IndexDocument { get; }
    public IEnumerable<CorsRule> CorsRules { get; }
    public Resource Resource => _bucket;

    private readonly Pulumi.Aws.S3.Bucket _bucket;
    private Pulumi.Aws.S3.BucketPublicAccessBlock? _publicAccessBlock;
    private Pulumi.Aws.S3.BucketVersioning? _versioning;
    private Pulumi.Aws.S3.BucketCorsConfiguration? _cors;

    // CloudFront CDN resources
    private Pulumi.Aws.CloudFront.Distribution? _distribution;
    private Pulumi.Aws.CloudFront.OriginAccessIdentity? _originAccessIdentity;

    public AwsBucket(
        string name,
        bool isPublic = false,
        BucketCdnConfiguration? cdn = null,
        bool versioningEnabled = false,
        IEnumerable<CorsRule>? corsRules = null,
        string? indexDocument = null,
        Pulumi.Aws.Provider? provider = null
    )
    {
        Name = name;
        IsPublic = isPublic;
        Cdn = cdn;
        VersioningEnabled = versioningEnabled;
        IndexDocument = indexDocument;
        CorsRules = corsRules ?? [];
        var customResourceOptions = new CustomResourceOptions { Provider = provider };

        // Create S3 bucket
        var bucketArgs = new Pulumi.Aws.S3.BucketArgs
        {
            BucketName = name,
            ForceDestroy = false,
        };

        if (IndexDocument is not null)
        {
            bucketArgs.Website = new Pulumi.Aws.S3.Inputs.BucketWebsiteArgs { IndexDocument = IndexDocument };
        }

        _bucket = new Pulumi.Aws.S3.Bucket(
            name,
            bucketArgs,
            customResourceOptions
        );

        // Configure public access block (required for public buckets)
        if (IsPublic)
        {
            _publicAccessBlock = new Pulumi.Aws.S3.BucketPublicAccessBlock(
                $"{name}-public-access",
                new Pulumi.Aws.S3.BucketPublicAccessBlockArgs
                {
                    Bucket = _bucket.Id,
                    BlockPublicAcls = false,
                    BlockPublicPolicy = false,
                    IgnorePublicAcls = false,
                    RestrictPublicBuckets = false
                },
                new CustomResourceOptions { DependsOn = new[] { _bucket }, Provider = provider }
            );

            // Add public read policy
            var publicReadPolicy = new Pulumi.Aws.S3.BucketPolicy(
                $"{name}-public-read-policy",
                new Pulumi.Aws.S3.BucketPolicyArgs
                {
                    Bucket = _bucket.Id,
                    Policy = _bucket.Arn.Apply(arn =>
                        JsonSerializer.Serialize(
                            new
                            {
                                Version = "2012-10-17",
                                Statement = new[]
                                {
                                    new
                                    {
                                        Sid = "PublicReadGetObject",
                                        Effect = "Allow",
                                        Principal = "*",
                                        Action = "s3:GetObject",
                                        Resource = $"{arn}/*"
                                    }
                                }
                            }
                        )
                    )
                },
                new CustomResourceOptions { DependsOn = new[] { _publicAccessBlock }, Provider = provider }
            );
        }

        // Configure versioning if enabled
        if (VersioningEnabled)
        {
            _versioning = new Pulumi.Aws.S3.BucketVersioning(
                $"{name}-versioning",
                new Pulumi.Aws.S3.BucketVersioningArgs
                {
                    Bucket = _bucket.Id,
                    VersioningConfiguration = new Pulumi.Aws.S3.Inputs.BucketVersioningVersioningConfigurationArgs
                    {
                        Status = "Enabled"
                    }
                },
                new CustomResourceOptions { DependsOn = new[] { _bucket }, Provider = provider }
            );
        }

        // Configure CORS if rules are provided
        if (CorsRules.Any())
        {
            var awsCorsRules = CorsRules
                .Select(rule => new Pulumi.Aws.S3.Inputs.BucketCorsConfigurationCorsRuleArgs
                {
                    AllowedHeaders = rule.AllowedHeaders.ToArray(),
                    AllowedMethods = rule.AllowedMethods.ToArray(),
                    AllowedOrigins = rule.AllowedOrigins.ToArray(),
                    ExposeHeaders = rule.ExposedHeaders.ToArray(),
                    MaxAgeSeconds = rule.MaxAgeSeconds
                })
                .ToArray();

            _cors = new Pulumi.Aws.S3.BucketCorsConfiguration(
                $"{name}-cors",
                new Pulumi.Aws.S3.BucketCorsConfigurationArgs
                {
                    Bucket = _bucket.Id,
                    CorsRules = awsCorsRules
                },
                new CustomResourceOptions { DependsOn = new[] { _bucket }, Provider = provider }
            );
        }

        // Configure CloudFront CDN if requested
        if (Cdn is not null)
        {
            // Create Origin Access Identity for secure S3 access
            _originAccessIdentity = new Pulumi.Aws.CloudFront.OriginAccessIdentity(
                $"{name}-oai",
                new Pulumi.Aws.CloudFront.OriginAccessIdentityArgs
                {
                    Comment = $"OAI for {name} bucket"
                },
                customResourceOptions
            );

            // Create bucket policy to allow CloudFront access
            var cdnBucketPolicy = new Pulumi.Aws.S3.BucketPolicy(
                $"{name}-cdn-policy",
                new Pulumi.Aws.S3.BucketPolicyArgs
                {
                    Bucket = _bucket.Id,
                    Policy = Output
                        .All(_bucket.Arn, _originAccessIdentity.IamArn)
                        .Apply(values =>
                        {
                            var bucketArn = values[0];
                            var oaiArn = values[1];
                            return JsonSerializer.Serialize(
                                new
                                {
                                    Version = "2012-10-17",
                                    Statement = new[]
                                    {
                                        new
                                        {
                                            Sid = "AllowCloudFrontAccess",
                                            Effect = "Allow",
                                            Principal = new { AWS = oaiArn },
                                            Action = "s3:GetObject",
                                            Resource = $"{bucketArn}/*"
                                        }
                                    }
                                }
                            );
                        })
                },
                new CustomResourceOptions
                {
                    DependsOn = new Resource[] { _bucket, _originAccessIdentity },
                    Provider = provider
                }
            );

            // Create CloudFront distribution
            var origins = new DistributionOriginArgs[]
            {
                new DistributionOriginArgs
                {
                    DomainName = _bucket.BucketRegionalDomainName,
                    OriginId = $"{name}-s3-origin",
                    S3OriginConfig = new DistributionOriginS3OriginConfigArgs
                    {
                        OriginAccessIdentity = _originAccessIdentity.CloudfrontAccessIdentityPath
                    }
                }
            };

            var defaultCacheBehavior = new DistributionDefaultCacheBehaviorArgs
            {
                TargetOriginId = $"{name}-s3-origin",
                ViewerProtocolPolicy = "redirect-to-https",
                AllowedMethods = new[]
                {
                    "DELETE",
                    "GET",
                    "HEAD",
                    "OPTIONS",
                    "PATCH",
                    "POST",
                    "PUT"
                },
                CachedMethods = new[] { "GET", "HEAD" },
                ForwardedValues = new DistributionDefaultCacheBehaviorForwardedValuesArgs
                {
                    QueryString = false,
                    Cookies = new DistributionDefaultCacheBehaviorForwardedValuesCookiesArgs
                    {
                        Forward = "none"
                    }
                },
                MinTtl = 0,
                DefaultTtl = 3600,
                MaxTtl = 86400
            };

            var distributionArgs = new Pulumi.Aws.CloudFront.DistributionArgs
            {
                Origins = origins,
                Enabled = true,
                IsIpv6Enabled = true,
                DefaultCacheBehavior = defaultCacheBehavior,
                Restrictions = new DistributionRestrictionsArgs
                {
                    GeoRestriction = new DistributionRestrictionsGeoRestrictionArgs
                    {
                        RestrictionType = "none"
                    }
                },
                ViewerCertificate = new DistributionViewerCertificateArgs
                {
                    CloudfrontDefaultCertificate = true
                }
            };

            if (IndexDocument is not null)
            {
                distributionArgs.DefaultRootObject = IndexDocument;
            }

            // Add custom domains if specified
            if (Cdn.Domains?.Any() == true)
            {
                distributionArgs.Aliases = Cdn.Domains.ToArray();
                // Note: For custom domains, you would need to provide your own SSL certificate
                // distributionArgs.ViewerCertificate = new DistributionViewerCertificateArgs
                // {
                //     AcmCertificateArn = "your-certificate-arn",
                //     SslSupportMethod = "sni-only"
                // };
            }

            _distribution = new Pulumi.Aws.CloudFront.Distribution(
                $"{name}-distribution",
                distributionArgs,
                new CustomResourceOptions
                {
                    DependsOn = new Resource[] { _bucket, _originAccessIdentity, cdnBucketPolicy },
                    Provider = provider
                }
            );
        }
    }

    public BucketDnsRecords GetRequiredDnsRecords(IEnumerable<string> hostNames)
    {
        var hostList = hostNames.ToList();

        List<DnsRecord> cdnRecords = [];
        List<DnsRecord> bucketRecords = [];

        if (_distribution is not null)
        {
            // When CDN is enabled, point domains to CloudFront distribution
            foreach (var host in hostList)
            {
                cdnRecords.Add(new DnsRecord(DnsRecordType.CNAME, host, _distribution.DomainName, $"{host}-cdn-cname"));
            }
        }
        else
        {
            // For S3, return CNAME records pointing to the S3 website endpoint or bucket endpoint
            foreach (var host in hostList)
            {
                bucketRecords.Add(
                    new DnsRecord(DnsRecordType.CNAME, host, _bucket.BucketRegionalDomainName, $"{host}-bucket-cname")
                );
            }
        }

        return new BucketDnsRecords(cdnRecords, bucketRecords);
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params BucketPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case BucketPermissions.ReadObject:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "s3:GetObject" },
                            Resources = new Output<string>[] { Arn.Apply(arn => $"{arn}/*") }
                        }
                    );
                    break;
                case BucketPermissions.WriteObject:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "s3:PutObject", "s3:DeleteObject" },
                            Resources = new Output<string>[] { Arn.Apply(arn => $"{arn}/*") }
                        }
                    );
                    break;
                case BucketPermissions.DeleteObject:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "s3:DeleteObject" },
                            Resources = new Output<string>[] { Arn.Apply(arn => $"{arn}/*") }
                        }
                    );
                    break;
                case BucketPermissions.ListBucket:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "s3:ListBucket" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case BucketPermissions.GetBucketMetadata:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "s3:GetBucketLocation", "s3:GetBucketVersioning" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case BucketPermissions.ManageBucket:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "s3:*" },
                            Resources = new Output<string>[] { Arn, Arn.Apply(arn => $"{arn}/*") }
                        }
                    );
                    break;
            }
        }
        return statements;
    }

    public Output<string> SyncDirectory(string localPath, string? keyPrefix = null, BucketSyncOptions? options = null)
    {
        var deployment = new AwsBucketDeployment($"{Name}-deploy", Name, localPath, keyPrefix, options, new ComponentResourceOptions { Parent = _bucket });
        return deployment.WebsiteUrl;
    }
}
