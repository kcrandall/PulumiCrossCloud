using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aiven;

public class AivenSearchDatabase : ISearchDatabase
{
    private readonly global::Pulumi.Aiven.OpenSearch _service;

    public AivenSearchDatabase(
        string name,
        Input<string> project,
        SearchDatabaseType type,
        string plan,
        string? cloud = null,
        global::Pulumi.Aiven.Provider? provider = null
    )
    {
        Name = name;
        Type = type;

        if (type != SearchDatabaseType.OpenSearch)
            throw new ArgumentException(
                $"Aiven provider currently only supports OpenSearch for ISearchDatabase. Requested: {type}"
            );

        _service = new global::Pulumi.Aiven.OpenSearch(
            name,
            new global::Pulumi.Aiven.OpenSearchArgs
            {
                Project = project,
                CloudName = cloud ?? "aws-eu-west-1",
                Plan = plan,
                ServiceName = name,
            },
            new CustomResourceOptions { Provider = provider }
        );
    }

    public string Name { get; }
    public Output<string> Arn => _service.Id;
    public CloudProvider Provider => CloudProvider.Aiven;

    public SearchDatabaseType Type { get; }

    public Output<string> Host => _service.ServiceHost;
    public Output<int> Port => _service.ServicePort;
    public Output<string> Username => _service.ServiceUsername.Apply(u => u ?? string.Empty);
    public Output<string> Password => _service.ServicePassword.Apply(p => p ?? string.Empty);
    public Output<string> ConnectionString => _service.ServiceUri;

    public IEnumerable<IPolicyStatement> GetPolicy(params SearchDatabasePermissions[] permissions)
    {
        // Aiven uses its own access control mechanism.
        // For now, we return an empty collection as there's no direct IAM equivalent.
        return [];
    }
}
