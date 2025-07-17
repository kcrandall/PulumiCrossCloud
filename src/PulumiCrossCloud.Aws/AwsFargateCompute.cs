using Pulumi;
using PulumiCrossCloud.Core;
using System.Text.Json;

namespace PulumiCrossCloud.Aws;

public class AwsFargateCompute : ICompute
{
    private readonly Pulumi.Aws.Ecs.Cluster _cluster;
    private readonly Pulumi.Aws.Ecs.Service _service;
    private readonly Pulumi.Aws.Ecs.TaskDefinition _taskDefinition;
    private readonly List<ISecret> _requiredSecrets;
    
    public AwsFargateCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.Fargate;
        _requiredSecrets = new List<ISecret>();
        
        _cluster = new Pulumi.Aws.Ecs.Cluster($"{name}-cluster");
        
        // Handle the registry URL as an Output
        var imageUri = containerRegistry is AwsContainerRegistry awsRegistry 
            ? awsRegistry.RegistryUrlOutput.Apply(url => $"{url}:{imageTag}")
            : Output.Create($"PLACEHOLDER_REGISTRY_URL:{imageTag}");
        
        _taskDefinition = new Pulumi.Aws.Ecs.TaskDefinition($"{name}-task", new Pulumi.Aws.Ecs.TaskDefinitionArgs
        {
            Family = name,
            Cpu = "256",
            Memory = "512",
            NetworkMode = "awsvpc",
            RequiresCompatibilities = new[] { "FARGATE" },
            ContainerDefinitions = imageUri.Apply(uri => JsonSerializer.Serialize(new[]  // Added System.Text.Json using
            {
                new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["image"] = uri,
                    ["portMappings"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["containerPort"] = 8080,
                            ["protocol"] = "tcp"
                        }
                    }
                }
            }))
        });
        
        _service = new Pulumi.Aws.Ecs.Service($"{name}-service", new Pulumi.Aws.Ecs.ServiceArgs
        {
            Cluster = _cluster.Id,
            TaskDefinition = _taskDefinition.Arn,
            LaunchType = "FARGATE",
            DesiredCount = 1
        });
    }
    
    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets;
    public Output<string> Arn => _service.Id; 
    public CloudProvider Provider => CloudProvider.AWS;
    
    public void AttachRole(IRole role)
    {
        var policies = new List<IPolicyStatement>();
        
        foreach (var secret in RequiredSecrets)
        {
            policies.AddRange(secret.GetPolicy(SecretPermissions.Read));
        }
        
        policies.AddRange(ContainerRegistry.GetPolicy(ContainerRegistryPermissions.Pull, ContainerRegistryPermissions.GetAuthToken));
        
        role.AttachInlinePolicy($"{Name}-policy", policies);
    }
    
    public void AddRequiredSecret(ISecret secret)
    {
        _requiredSecrets.Add(secret);
    }
    
    public void Start() { /* Update desired count to > 0 */ }
    public void Stop() { /* Update desired count to 0 */ }
    public void Scale(int replicas) { /* Update desired count */ }
    public void UpdateConfiguration(IDictionary<string, object> config) { /* Update task definition */ }
    
    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ComputePermissions.GetStatus:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "ecs:DescribeServices", "ecs:DescribeTasks" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.Scale:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "ecs:UpdateService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.UpdateConfiguration:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "ecs:UpdateService", "ecs:RegisterTaskDefinition" },
                        Resources = new Output<string>[] { Output.Create("*") }
                    });
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "logs:GetLogEvents", "logs:DescribeLogGroups", "logs:DescribeLogStreams" },
                        Resources = new Output<string>[] { Output.Create($"arn:aws:logs:*:*:log-group:/ecs/{Name}/*") }
                    });
                    break;
            }
        }
        
        return statements;
    }
}