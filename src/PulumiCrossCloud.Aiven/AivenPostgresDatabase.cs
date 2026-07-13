using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aiven;

public class AivenPostgresDatabase : IPostgresDatabase
{
    private readonly global::Pulumi.Aiven.Pg _service;

    public AivenPostgresDatabase(
        string name,
        Input<string> project,
        string plan,
        string? cloud = null,
        global::Pulumi.Aiven.Provider? provider = null
    )
    {
        Name = name;
        _service = new global::Pulumi.Aiven.Pg(
            name,
            new global::Pulumi.Aiven.PgArgs
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
    public CloudProvider Provider => CloudProvider.Aiven;
    public Output<string> Arn => _service.Id;

    public Output<string> Host => _service.ServiceHost;
    public Output<int> Port => _service.ServicePort;
    public Output<string> Username => _service.ServiceUsername.Apply(u => u ?? string.Empty);
    public Output<string> Password => _service.ServicePassword.Apply(p => p ?? string.Empty);
    public Output<string> ConnectionString => _service.ServiceUri;

    public Output<string> DatabaseName => Output.Create("defaultdb");

    public void EnableExtension(string extensionName)
    {
        // Aiven extensions are managed via service integration or SQL.
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params PostgresPermissions[] permissions)
    {
        return [];
    }
}
