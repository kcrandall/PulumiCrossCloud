using Pulumi;
using Pulumi.Gcp.Projects;
using Pulumi.Command.Local;
using PulumiCrossCloud.Core;
using System.Collections.Concurrent;
using Pulumi.DockerBuild.Inputs;

namespace PulumiCrossCloud.Gcp;

public class GcpContainerRegistry : IContainerRegistry
{
    private static readonly ConcurrentDictionary<string, Service> _artifactRegistryApiEnabled = new();
    private static readonly HashSet<string> _importedHelloWorldImages = new();
    private readonly Pulumi.Gcp.ArtifactRegistry.Repository _repository;
    private readonly string _projectId;
    private readonly string _region;
    private readonly bool _useDefaultHelloWorldImage;
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="name">The resource name</param>
    /// <param name="useDefaultHelloWorldImage"></param>
    public GcpContainerRegistry(string name, string repositoryId, bool useDefaultHelloWorldImage = false)
    {
        Name = name;
        RepositoryId = repositoryId;
        _projectId = GcpProjectConfig.ProjectId;
        _region = GcpProjectConfig.Region;
        _useDefaultHelloWorldImage = useDefaultHelloWorldImage;
        // Ensure Artifact Registry API is enabled for this project
        if (!_artifactRegistryApiEnabled.ContainsKey(_projectId))
        {
            var enableApi = new Service($"artifactregistry-api-{_projectId}", new ServiceArgs
            {
                Project = _projectId,
                ServiceName = "artifactregistry.googleapis.com",
                DisableDependentServices = false
            });
            _artifactRegistryApiEnabled[_projectId] = enableApi;
        }
        var dependsOn = new[] { _artifactRegistryApiEnabled[_projectId] };
        _repository = new Pulumi.Gcp.ArtifactRegistry.Repository(name, new Pulumi.Gcp.ArtifactRegistry.RepositoryArgs
        {
            Format = "DOCKER",
            RepositoryId = repositoryId,
            Location = _region
        }, new CustomResourceOptions { DependsOn = dependsOn });

        if (useDefaultHelloWorldImage)
        {
            ImportHelloWorldImage();
        }
    }
    
    public string Name { get; }
    public string RepositoryId { get; }
    public Output<string> Arn => _repository.Name;
    public Resource Resource => _repository;
    public CloudProvider Provider => CloudProvider.GCP;
    public string RegistryUrl => throw new NotImplementedException("Use RegistryUrlOutput property for Pulumi deployments");
    public Output<string> RegistryUrlOutput => _repository.Name.Apply(name => $"{_region}-docker.pkg.dev/{_projectId}/{RepositoryId}/{RepositoryId}");

    public void ImportHelloWorldImage()
    {
        var repoPath = $"{_region}-docker.pkg.dev/{_projectId}/{RepositoryId}";
        var registryUrl = $"{repoPath}:latest";
        // Only run if the registry is completely empty
        var checkEmptyCmd = $"[ $(gcloud artifacts docker images list {repoPath} --format='get(NAME)' | wc -l) -eq 0 ]";
        var pullCmd = "docker pull gcr.io/google-samples/hello-app:1.0";
        var tagCmd = $"docker tag gcr.io/google-samples/hello-app:1.0 {registryUrl}";
        var pushCmd = $"docker push {registryUrl}";
        var fullCmd = $"{checkEmptyCmd} && {pullCmd} && {tagCmd} && {pushCmd}";
        var importImage = new Pulumi.Command.Local.Command($"import-hello-world-image-{RepositoryId}", new Pulumi.Command.Local.CommandArgs
        {
            Create = fullCmd,
            Triggers = new[] { registryUrl }
        }, new CustomResourceOptions { DependsOn = new[] { _repository } });
    }
    
    public InputList<RegistryArgs> GetDockerBuildRegistryConfig()
    {
        // GCP uses default gcloud authentication, no explicit credentials needed
        return new InputList<RegistryArgs>();
    }
    
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