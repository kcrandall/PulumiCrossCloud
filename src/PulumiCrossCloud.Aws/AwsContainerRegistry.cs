using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsContainerRegistry : IContainerRegistry
{
    private readonly Pulumi.Aws.Ecr.Repository _repository;
    
    public AwsContainerRegistry(string name)
    {
        Name = name;
        _repository = new Pulumi.Aws.Ecr.Repository(name, new Pulumi.Aws.Ecr.RepositoryArgs
        {
            Name = name
        });
    }
    
    public string Name { get; }
    public Output<string> Arn => _repository.Arn;
    public CloudProvider Provider => CloudProvider.AWS;
    
    public string RegistryUrl => throw new NotImplementedException("Use RegistryUrlOutput property for Pulumi deployments");
    public Output<string> RegistryUrlOutput => _repository.RepositoryUrl;
    
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
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { 
                            "ecr:GetDownloadUrlForLayer",
                            "ecr:BatchGetImage",
                            "ecr:BatchCheckLayerAvailability"
                        },
                        Resources = new Output<string>[] { Arn }  // Using Output<string>[]
                    });
                    break;
                case ContainerRegistryPermissions.Push:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { 
                            "ecr:PutImage",
                            "ecr:InitiateLayerUpload",
                            "ecr:UploadLayerPart",
                            "ecr:CompleteLayerUpload",
                            "ecr:BatchCheckLayerAvailability"
                        },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.GetAuthToken:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "ecr:GetAuthorizationToken" },
                        Resources = new Output<string>[] { Output.Create("*") }  // Convert string to Output<string>
                    });
                    break;
                case ContainerRegistryPermissions.List:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "ecr:DescribeRepositories", "ecr:ListImages" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.Delete:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "ecr:BatchDeleteImage", "ecr:DeleteRepository" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.GetMetadata:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "ecr:DescribeImages", "ecr:GetRepositoryPolicy" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ContainerRegistryPermissions.ManageRepository:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { 
                            "ecr:CreateRepository",
                            "ecr:DeleteRepository",
                            "ecr:SetRepositoryPolicy",
                            "ecr:DeleteRepositoryPolicy"
                        },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
            }
        }
        
        return statements;
    }
}