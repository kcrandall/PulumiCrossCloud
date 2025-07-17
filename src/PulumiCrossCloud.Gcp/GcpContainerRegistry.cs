using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpContainerRegistry : IContainerRegistry
{
    private readonly Pulumi.Gcp.ArtifactRegistry.Repository _repository;
    private readonly string _projectId;
    private readonly string _region;
    
    public GcpContainerRegistry(string name)
    {
        Name = name;
        _projectId = GcpProjectConfig.ProjectId;
        _region = GcpProjectConfig.Region;
        _repository = new Pulumi.Gcp.ArtifactRegistry.Repository(name, new Pulumi.Gcp.ArtifactRegistry.RepositoryArgs
        {
            Format = "DOCKER",
            RepositoryId = name,
            Location = "us-central1"
        });
    }
    
    public string Name { get; }
    public Output<string> Arn => _repository.Name;
    public CloudProvider Provider => CloudProvider.GCP;
    public string RegistryUrl => throw new NotImplementedException("Use RegistryUrlOutput property for Pulumi deployments");
    public Output<string> RegistryUrlOutput => _repository.Name.Apply(name => $"us-central1-docker.pkg.dev/PROJECT-ID/{Name}");
    
    public void PushImage(string imageName, string tag) { /* Implementation */ }
    public void PullImage(string imageName, string tag) { /* Implementation */ }
    public IEnumerable<string> ListRepositories() => new List<string>();
    public void DeleteRepository(string repositoryName) { /* Implementation */ }
    
    public IEnumerable<IPolicyStatement> GetPolicy(params ContainerRegistryPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ContainerRegistryPermissions.Pull:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/artifactregistry.reader" },
                        Resources = new Output<string>[] { Arn }  // Fixed to use Output<string>[]
                    });
                    break;
                case ContainerRegistryPermissions.Push:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/artifactregistry.writer" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.List:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/artifactregistry.reader" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.Delete:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/artifactregistry.repoAdmin" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.GetMetadata:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/artifactregistry.reader" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.ManageRepository:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/artifactregistry.admin" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.GetAuthToken:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/artifactregistry.reader" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
            }
        }
        
        return statements;
    }
}