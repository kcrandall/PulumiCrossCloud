using Pulumi;
using Pulumi.Aws.OpenSearch;
using Pulumi.Aws.OpenSearch.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsSearchDatabase : ISearchDatabase
{
    private readonly ServerlessCollectionGroup _collectionGroup;
    private readonly ServerlessCollection _collection;

    public AwsSearchDatabase(
        string name,
        SearchDatabaseType type,
        ISearchDatabaseOptions? options = null,
        Pulumi.Aws.Provider? provider = null
    )
    {
        if (type != SearchDatabaseType.OpenSearch)
        {
            throw new ArgumentException($"AWS provider currently only supports OpenSearch for ISearchDatabase. Requested: {type}");
        }

        Name = name;
        Type = type;

        var awsOptions = options as AwsSearchDatabaseOptions ?? new AwsSearchDatabaseOptions();
        ValidateOptions(awsOptions);

        var customResourceOptions = new CustomResourceOptions { Provider = provider };
        var groupName = awsOptions.CollectionGroupName ?? $"{name}-group";
        var collectionName = awsOptions.CollectionName ?? name;

        var groupArgs = new ServerlessCollectionGroupArgs
        {
            Name = groupName,
            Generation = awsOptions.Generation,
            StandbyReplicas = awsOptions.StandbyReplicas,
            CapacityLimits =
            {
                new ServerlessCollectionGroupCapacityLimitArgs
                {
                    MinIndexingCapacityInOcu = awsOptions.MinIndexingCapacityInOcu,
                    MaxIndexingCapacityInOcu = awsOptions.MaxIndexingCapacityInOcu,
                    MinSearchCapacityInOcu = awsOptions.MinSearchCapacityInOcu,
                    MaxSearchCapacityInOcu = awsOptions.MaxSearchCapacityInOcu,
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(awsOptions.Description))
        {
            groupArgs.Description = awsOptions.Description;
        }

        if (!string.IsNullOrWhiteSpace(awsOptions.Region))
        {
            groupArgs.Region = awsOptions.Region;
        }

        if (awsOptions.Tags is not null)
        {
            groupArgs.Tags = CreateTags(awsOptions.Tags);
        }

        _collectionGroup = new ServerlessCollectionGroup(
            $"{name}-group",
            groupArgs,
            customResourceOptions
        );

        var encryptionConfig = new ServerlessCollectionEncryptionConfigArgs();
        if (awsOptions.KmsKeyArn is null)
        {
            encryptionConfig.AwsOwnedKey = awsOptions.AwsOwnedKey;
        }
        else
        {
            encryptionConfig.KmsKeyArn = awsOptions.KmsKeyArn;
        }

        var collectionArgs = new ServerlessCollectionArgs
        {
            Name = collectionName,
            Type = awsOptions.CollectionType,
            CollectionGroupName = _collectionGroup.Name,
            StandbyReplicas = awsOptions.StandbyReplicas,
            EncryptionConfigs = { encryptionConfig },
        };

        var vectorOptions = CreateVectorOptions(awsOptions);
        if (vectorOptions is not null)
        {
            collectionArgs.VectorOptions = vectorOptions;
        }

        if (!string.IsNullOrWhiteSpace(awsOptions.Description))
        {
            collectionArgs.Description = awsOptions.Description;
        }

        if (!string.IsNullOrWhiteSpace(awsOptions.Region))
        {
            collectionArgs.Region = awsOptions.Region;
        }

        if (awsOptions.Tags is not null)
        {
            collectionArgs.Tags = CreateTags(awsOptions.Tags);
        }

        _collection = new ServerlessCollection(name, collectionArgs, customResourceOptions);
    }

    public string Name { get; }
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> Arn => _collection.Arn;
    public SearchDatabaseType Type { get; }
    public Output<string> Host => _collection.CollectionEndpoint.Apply(GetHost);
    public Output<int> Port => Output.Create(443);
    public Output<string> Username => Output.Create(string.Empty);
    public Output<string> Password => Output.Create(string.Empty);
    public Output<string> ConnectionString => _collection.CollectionEndpoint;
    public ServerlessCollectionGroup CollectionGroup => _collectionGroup;
    public ServerlessCollection Collection => _collection;

    public IEnumerable<IPolicyStatement> GetPolicy(params SearchDatabasePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();

        foreach (var permission in permissions)
        {
            var actions = permission switch
            {
                SearchDatabasePermissions.Admin => new[] { "aoss:*" },
                _ => new[] { "aoss:APIAccessAll" }
            };

            statements.Add(new AwsPolicyStatement
            {
                Effect = "Allow",
                Actions = actions,
                Resources = [Arn],
                ResourceNames = [Name]
            });
        }

        return statements;
    }

    private static List<ServerlessCollectionVectorOptionArgs>? CreateVectorOptions(AwsSearchDatabaseOptions options)
    {
        if (!string.Equals(options.CollectionType, "VECTORSEARCH", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.ServerlessVectorAcceleration))
        {
            return null;
        }

        return
        [
            new ServerlessCollectionVectorOptionArgs
            {
                ServerlessVectorAcceleration = options.ServerlessVectorAcceleration
            }
        ];
    }

    private static void ValidateOptions(AwsSearchDatabaseOptions options)
    {
        if (
            string.Equals(options.Generation, "NEXTGEN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.StandbyReplicas, "ENABLED", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new ArgumentException("OpenSearch Serverless NEXTGEN collection groups require StandbyReplicas to be ENABLED.");
        }

        if (
            string.Equals(options.Generation, "NEXTGEN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.CollectionType, "SEARCH", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.CollectionType, "VECTORSEARCH", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new ArgumentException("OpenSearch Serverless NEXTGEN collections support SEARCH and VECTORSEARCH collection types.");
        }

        if (options.KmsKeyArn is null && !options.AwsOwnedKey)
        {
            throw new ArgumentException("OpenSearch Serverless encryption requires either KmsKeyArn or AwsOwnedKey.");
        }
    }

    private static InputMap<string> CreateTags(IDictionary<string, string> tags)
    {
        var inputMap = new InputMap<string>();
        foreach (var tag in tags)
        {
            inputMap.Add(tag.Key, tag.Value);
        }

        return inputMap;
    }

    private static string GetHost(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return endpoint
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split('/')[0];
    }
}
