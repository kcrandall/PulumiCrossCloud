// using Pulumi;
// using PulumiCrossCloud.Core;
// using System.Text.Json;
//
// namespace PulumiCrossCloud.Aws;
//
// public class AwsFargateCompute : ICompute
// {
//     private readonly Pulumi.Aws.Ecs.Cluster _cluster;
//     private Pulumi.Aws.Ecs.Service _service;
//     private Pulumi.Aws.Ecs.TaskDefinition _taskDefinition;
//     private readonly List<ISecret> _requiredSecrets;
//     private readonly Dictionary<string, string> _environmentVariables;
//     private Pulumi.Aws.Iam.Role? _executionRole;
//     private readonly List<IPolicyStatement> _customPolicyStatements = new();
//     
//     public AwsFargateCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
//     {
//         Name = name;
//         ContainerRegistry = containerRegistry;
//         ServiceType = ComputeServiceType.Fargate;
//         _requiredSecrets = new List<ISecret>();
//         _environmentVariables = new Dictionary<string, string>();
//         // Create execution role automatically
//         _executionRole = new Pulumi.Aws.Iam.Role($"{name}-execution-role", new Pulumi.Aws.Iam.RoleArgs
//         {
//             AssumeRolePolicy = @"{
//                 ""Version"": ""2012-10-17"",
//                 ""Statement"": [{
//                     ""Effect"": ""Allow"",
//                     ""Principal"": {
//                         ""Service"": ""ecs-tasks.amazonaws.com""
//                     },
//                     ""Action"": ""sts:AssumeRole""
//                 }]
//             }"
//         });
//         _cluster = new Pulumi.Aws.Ecs.Cluster($"{name}-cluster");
//         // Service will be created after Build (not here)
//     }
//     
//     public string Name { get; }
//     public ComputeServiceType ServiceType { get; }
//     public IContainerRegistry ContainerRegistry { get; }
//     public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets;
//     public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
//     public Output<string> Arn => _service.Id;
//     public CloudProvider Provider => CloudProvider.AWS;
//     public int MinCount { get; set; } = 1;
//     public int MaxCount { get; set; } = 1;
//     public Output<string> ServiceUrl => Output.Create(""); // ECS does not provide a direct service URL
//
//     public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
//     
//     public void AddRequiredSecret(ISecret secret)
//     {
//         _requiredSecrets.Add(secret);
//     }
//     
//     public void AddPolicyStatement(IPolicyStatement statement)
//     {
//         _customPolicyStatements.Add(statement);
//     }
//
//     public void Build()
//     {
//         if (_executionRole == null) return;
//         // Aggregate all policy statements for the execution role
//         var allPolicies = new List<IPolicyStatement>();
//         foreach (var secret in RequiredSecrets)
//         {
//             allPolicies.AddRange(secret.GetPolicy(SecretPermissions.Read));
//         }
//         allPolicies.AddRange(ContainerRegistry.GetPolicy(
//             ContainerRegistryPermissions.Pull,
//             ContainerRegistryPermissions.GetAuthToken
//         ));
//         if (_customPolicyStatements.Any())
//         {
//             allPolicies.AddRange(_customPolicyStatements);
//         }
//         if (allPolicies.Any())
//         {
//             var policyDocument = CreatePolicyDocument(allPolicies);
//             new Pulumi.Aws.Iam.RolePolicy($"{Name}-execution-policy", new Pulumi.Aws.Iam.RolePolicyArgs
//             {
//                 Role = _executionRole.Name,
//                 Policy = policyDocument
//             });
//         }
//         // Create or update ECS Task Definition and Service with latest env vars and secrets
//         var imageUri = ContainerRegistry is AwsContainerRegistry awsRegistry 
//             ? awsRegistry.RegistryUrlOutput.Apply(url => $"{url}:latest")
//             : Output.Create($"PLACEHOLDER_REGISTRY_URL:latest");
//         var containerDef = imageUri.Apply(uri => JsonSerializer.Serialize(new[]
//         {
//             new Dictionary<string, object>
//             {
//                 ["name"] = Name,
//                 ["image"] = uri,
//                 ["environment"] = EnvironmentVariables.Select(kvp => new Dictionary<string, object>
//                 {
//                     ["name"] = kvp.Key,
//                     ["value"] = kvp.Value
//                 }).ToArray(),
//                 ["secrets"] = RequiredSecrets.Select(secret => new Dictionary<string, object>
//                 {
//                     ["name"] = secret.Name,
//                     ["valueFrom"] = secret is AwsSecret awsSecret ? awsSecret.Arn : ""
//                 }).ToArray(),
//                 ["portMappings"] = new[]
//                 {
//                     new Dictionary<string, object>
//                     {
//                         ["containerPort"] = 8080,
//                         ["protocol"] = "tcp"
//                     }
//                 },
//                 ["healthCheck"] = HealthCheckConfiguration != null ? new Dictionary<string, object>
//                 {
//                     ["command"] = new[] { "CMD-SHELL", $"curl -f http://localhost:{HealthCheckConfiguration.Path ?? "8080"} || exit 1" },
//                     ["interval"] = HealthCheckConfiguration.IntervalSeconds,
//                     ["timeout"] = HealthCheckConfiguration.TimeoutSeconds,
//                     ["retries"] = HealthCheckConfiguration.UnhealthyThreshold,
//                     ["startPeriod"] = HealthCheckConfiguration.HealthyThreshold
//                 } : null
//             }
//         }));
//         _taskDefinition = new Pulumi.Aws.Ecs.TaskDefinition($"{Name}-task", new Pulumi.Aws.Ecs.TaskDefinitionArgs
//         {
//             Family = Name,
//             Cpu = "256",
//             Memory = "512",
//             NetworkMode = "awsvpc",
//             RequiresCompatibilities = new[] { "FARGATE" },
//             ExecutionRoleArn = _executionRole.Arn,
//             ContainerDefinitions = containerDef
//         });
//         _service = new Pulumi.Aws.Ecs.Service($"{Name}-service", new Pulumi.Aws.Ecs.ServiceArgs
//         {
//             Cluster = _cluster.Id,
//             TaskDefinition = _taskDefinition.Arn,
//             LaunchType = "FARGATE",
//             DesiredCount = MinCount // Use MinCount for initial desired count
//         });
//     }
//
//     public void AddEnvironmentVariable(string name, string value)
//     {
//         _environmentVariables[name] = value;
//     }
//
//     public void AddEnvironmentVariables(IDictionary<string, string> variables)
//     {
//         foreach (var kvp in variables)
//         {
//             _environmentVariables[kvp.Key] = kvp.Value;
//         }
//     }
//     
//     private Output<string> CreatePolicyDocument(IEnumerable<IPolicyStatement> statements)
//     {
//         var statementsList = statements.ToList();
//         var allResourceOutputs = statementsList.SelectMany(s => s.Resources).ToArray();
//         
//         return Output.All(allResourceOutputs).Apply(allResources =>
//         {
//             var resourceIndex = 0;
//             var policyStatements = new List<object>();
//             
//             foreach (var statement in statementsList)
//             {
//                 var statementResourceCount = statement.Resources.Count();
//                 var statementResources = allResources.Skip(resourceIndex).Take(statementResourceCount).ToArray();
//                 resourceIndex += statementResourceCount;
//                 
//                 policyStatements.Add(new
//                 {
//                     Effect = statement.Effect,
//                     Action = statement.Actions.ToArray(),
//                     Resource = statementResources
//                 });
//             }
//             
//             var policy = new
//             {
//                 Version = "2012-10-17",
//                 Statement = policyStatements
//             };
//             
//             return JsonSerializer.Serialize(policy);
//         });
//     }
//     
//     public void Start() { /* ECS Service is started by default, implement if needed */ }
//     public void Stop() { /* ECS Service stop logic if needed */ }
//     public void Scale(int replicas)
//     {
//         // Update desired count, but respect MinCount/MaxCount
//         var count = Math.Max(MinCount, Math.Min(MaxCount, replicas));
//         // ECS Service update logic would go here
//     }
//     
//     public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
//     {
//         var statements = new List<IPolicyStatement>();
//         foreach (var permission in permissions)
//         {
//             switch (permission)
//             {
//                 case ComputePermissions.GetStatus:
//                     statements.Add(new AwsPolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "ecs:DescribeServices", "ecs:DescribeTasks" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ComputePermissions.Scale:
//                     statements.Add(new AwsPolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "ecs:UpdateService" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ComputePermissions.UpdateConfiguration:
//                     statements.Add(new AwsPolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "ecs:UpdateService", "ecs:RegisterTaskDefinition" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ComputePermissions.Start:
//                     statements.Add(new AwsPolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "ecs:UpdateService" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ComputePermissions.Stop:
//                     statements.Add(new AwsPolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "ecs:UpdateService" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ComputePermissions.GetLogs:
//                     statements.Add(new AwsPolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "logs:GetLogEvents", "logs:DescribeLogStreams" },
//                         Resources = new Output<string>[] { Output.Create($"arn:aws:logs:*:*:log-group:/aws/ecs/{Name}/*") }
//                     });
//                     break;
//             }
//         }
//         return statements;
//     }
// }