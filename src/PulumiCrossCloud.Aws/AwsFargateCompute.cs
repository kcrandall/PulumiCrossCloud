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
    private readonly Dictionary<string, string> _environmentVariables;
    private Pulumi.Aws.Iam.Role? _executionRole;
    
    public AwsFargateCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.Fargate;
        _requiredSecrets = new List<ISecret>();
        _environmentVariables = new Dictionary<string, string>();
        
        // Create execution role automatically
        _executionRole = new Pulumi.Aws.Iam.Role($"{name}-execution-role", new Pulumi.Aws.Iam.RoleArgs
        {
            AssumeRolePolicy = @"{
                ""Version"": ""2012-10-17"",
                ""Statement"": [{
                    ""Effect"": ""Allow"",
                    ""Principal"": {
                        ""Service"": ""ecs-tasks.amazonaws.com""
                    },
                    ""Action"": ""sts:AssumeRole""
                }]
            }"
        });
        
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
            ExecutionRoleArn = _executionRole.Arn,
            ContainerDefinitions = imageUri.Apply(uri => JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["image"] = uri,
                    ["environment"] = _environmentVariables.Select(kvp => new Dictionary<string, object>
                    {
                        ["name"] = kvp.Key,
                        ["value"] = kvp.Value
                    }).ToArray(),
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
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
    public Output<string> Arn => _service.Id;
    public CloudProvider Provider => CloudProvider.AWS;
    
    public void AddRequiredSecret(ISecret secret)
    {
        _requiredSecrets.Add(secret);
    }
    
    public void AddEnvironmentVariable(string name, string value)
    {
        _environmentVariables[name] = value;
    }
    
    public void AddEnvironmentVariables(IDictionary<string, string> variables)
    {
        foreach (var kvp in variables)
        {
            _environmentVariables[kvp.Key] = kvp.Value;
        }
    }
    
    public void ConfigurePermissions()
    {
        if (_executionRole == null) return;
        
        var policies = new List<IPolicyStatement>();
        
        // Automatically add read permissions for all required secrets
        foreach (var secret in RequiredSecrets)
        {
            policies.AddRange(secret.GetPolicy(SecretPermissions.Read));
        }
        
        // Add policies for container registry
        policies.AddRange(ContainerRegistry.GetPolicy(
            ContainerRegistryPermissions.Pull, 
            ContainerRegistryPermissions.GetAuthToken
        ));
        
        // Create and attach the policy
        if (policies.Any())
        {
            var policyDocument = CreatePolicyDocument(policies);
            
            new Pulumi.Aws.Iam.RolePolicy($"{Name}-permissions", new Pulumi.Aws.Iam.RolePolicyArgs
            {
                Role = _executionRole.Name,
                Policy = policyDocument
            });
        }
    }
    
    private Output<string> CreatePolicyDocument(IEnumerable<IPolicyStatement> statements)
    {
        var statementsList = statements.ToList();
        var allResourceOutputs = statementsList.SelectMany(s => s.Resources).ToArray();
        
        return Output.All(allResourceOutputs).Apply(allResources =>
        {
            var resourceIndex = 0;
            var policyStatements = new List<object>();
            
            foreach (var statement in statementsList)
            {
                var statementResourceCount = statement.Resources.Count();
                var statementResources = allResources.Skip(resourceIndex).Take(statementResourceCount).ToArray();
                resourceIndex += statementResourceCount;
                
                policyStatements.Add(new
                {
                    Effect = statement.Effect,
                    Action = statement.Actions.ToArray(),
                    Resource = statementResources
                });
            }
            
            var policy = new
            {
                Version = "2012-10-17",
                Statement = policyStatements
            };
            
            return JsonSerializer.Serialize(policy);
        });
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