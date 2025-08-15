using System.Collections.Concurrent;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Pulumi;
using Pulumi.Command.Local;
using Pulumi.DockerBuild.Inputs;
using Pulumi.Gcp.Projects;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpContainerRegistry : IContainerRegistry
{
    private static readonly HashSet<string> _importedHelloWorldImages = new();
    private readonly Pulumi.Gcp.ArtifactRegistry.Repository _repository;
    private Resource _readyResource;
    private readonly string _projectId;
    private readonly string _region;
    private readonly bool _useDefaultHelloWorldImage;

    /// <summary>
    ///
    /// </summary>
    /// <param name="name">The resource name</param>
    /// <param name="useDefaultHelloWorldImage"></param>
    public GcpContainerRegistry(string name, bool useDefaultHelloWorldImage = false)
    {
        Name = name;
        RepositoryId = name;
        _projectId = GcpProjectConfig.ProjectId;
        _region = GcpProjectConfig.Region;
        _useDefaultHelloWorldImage = useDefaultHelloWorldImage;
        // Ensure Artifact Registry API is enabled for this project (via central manager)
        var artifactRegistryService = GcpProjectServices.EnsureEnabled(
            "artifactregistry.googleapis.com"
        );
        var dependsOn = new[] { artifactRegistryService };
        _repository = new Pulumi.Gcp.ArtifactRegistry.Repository(
            name,
            new Pulumi.Gcp.ArtifactRegistry.RepositoryArgs
            {
                Format = "DOCKER",
                RepositoryId = RepositoryId,
                Project = _projectId,
                Location = _region
            },
            new CustomResourceOptions { DependsOn = dependsOn }
        );
        _readyResource = _repository;

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
    public string RegistryUrl =>
        throw new NotImplementedException("Use RegistryUrlOutput property for Pulumi deployments");
    public Output<string> RegistryUrlOutput =>
        _repository.Name.Apply(name => $"{_region}-docker.pkg.dev/{_projectId}/{RepositoryId}");

    public void ImportHelloWorldImage()
    {
        // var address = $"{_region}-docker.pkg.dev";
        // var repoPath = $"{address}/{_projectId}/{RepositoryId}";
        // var registryUrl = $"{repoPath}/keycloak:latest";

        // // Get access token via ADC to login docker to Artifact Registry
        // var token = Output.Create(
        //     Task.Run(async () =>
        //     {
        //         var credential = await GoogleCredential.GetApplicationDefaultAsync();
        //         if (credential.IsCreateScopedRequired)
        //         {
        //             credential = credential.CreateScoped(
        //                 new[] { "https://www.googleapis.com/auth/cloud-platform" }
        //             );
        //         }
        //         var access = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        //         return access ?? string.Empty;
        //     })
        // );

        // var dockerLogin = new Pulumi.Command.Local.Command(
        //     $"docker-login-{RepositoryId}",
        //     new Pulumi.Command.Local.CommandArgs
        //     {
        //         Create =
        //             $"sh -c \"echo \"$GOOGLE_ACCESS_TOKEN\" | docker login -u oauth2accesstoken --password-stdin https://{address}\"",
        //         Environment = { { "GOOGLE_ACCESS_TOKEN", token } },
        //         Triggers = new[] { address }
        //     },
        //     new CustomResourceOptions { DependsOn = new[] { _repository } }
        // );

        // // Pull, tag, and push hello world image
        // var pullCmd = "docker pull gcr.io/google-samples/hello-app:1.0";
        // var tagCmd = $"docker tag gcr.io/google-samples/hello-app:1.0 {registryUrl}";
        // var pushCmd = $"docker push {registryUrl}";
        // var fullCmd = $"{pullCmd} && {tagCmd} && {pushCmd}";
        // var importImage = new Pulumi.Command.Local.Command(
        //     $"import-hello-world-image-{RepositoryId}",
        //     new Pulumi.Command.Local.CommandArgs
        //     {
        //         Create = fullCmd,
        //         Triggers = new[] { registryUrl }
        //     },
        //     new CustomResourceOptions { DependsOn = new Resource[] { _repository, dockerLogin } }
        // );
        // _readyResource = importImage;
    }

    public InputList<RegistryArgs> GetDockerBuildRegistryConfig()
    {
        // Get access token via ADC for Docker build authentication
        var token = Output.Create(
            Task.Run(async () =>
            {
                var credential = await GoogleCredential.GetApplicationDefaultAsync();
                if (credential.IsCreateScopedRequired)
                {
                    credential = credential.CreateScoped(
                        new[] { "https://www.googleapis.com/auth/cloud-platform" }
                    );
                }
                var access = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
                return access ?? string.Empty;
            })
        );

        return new InputList<RegistryArgs>
        {
            new RegistryArgs
            {
                Address = $"{_region}-docker.pkg.dev",
                Username = "oauth2accesstoken",
                Password = token
            }
        };
    }

    public void PushImage(
        string imageName,
        string tag
    ) { /* Implementation */
    }

    public void PullImage(
        string imageName,
        string tag
    ) { /* Implementation */
    }

    public IEnumerable<string> ListRepositories() => new List<string>();

    public void DeleteRepository(
        string repositoryName
    ) { /* Implementation */
    }

    public IEnumerable<IPolicyStatement> GetPolicy(
        params ContainerRegistryPermissions[] permissions
    )
    {
        var statements = new List<IPolicyStatement>();

        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ContainerRegistryPermissions.Pull:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/artifactregistry.reader" },
                            Resources = new Output<string>[] { Arn } // Fixed to use Output<string>[]
                        }
                    );
                    break;
                case ContainerRegistryPermissions.Push:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/artifactregistry.writer" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case ContainerRegistryPermissions.List:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/artifactregistry.reader" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case ContainerRegistryPermissions.Delete:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/artifactregistry.repoAdmin" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case ContainerRegistryPermissions.GetMetadata:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/artifactregistry.reader" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case ContainerRegistryPermissions.ManageRepository:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/artifactregistry.admin" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case ContainerRegistryPermissions.GetAuthToken:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/artifactregistry.reader" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
            }
        }

        return statements;
    }
}
