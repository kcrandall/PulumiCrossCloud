using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsAppRunnerCompute : ICompute
{
    private readonly List<ISecret> _requiredSecrets;
    private readonly Dictionary<string, string> _environmentVariables;
    private Pulumi.Aws.Iam.Role? _instanceRole;
    private Pulumi.Aws.Iam.Role? _accessRole;
    private Pulumi.Aws.AppRunner.Service? _service;
    private readonly string _name;
    private readonly IContainerRegistry _containerRegistry;
    private readonly string _imageTag;
    private readonly List<IPolicyStatement> _customPolicyStatements = new();
    private Pulumi.Aws.AppRunner.AutoScalingConfigurationVersion? _autoScalingConfig;

    public AwsAppRunnerCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
    {
        _name = name;
        _containerRegistry = containerRegistry;
        _imageTag = imageTag;
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.AppRunner;
        _requiredSecrets = new List<ISecret>();
        _environmentVariables = new Dictionary<string, string>();

        // Don't create the service yet - wait until ConfigurePermissions is called
    }

    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets;
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
    public Output<string> Arn => _service?.Arn ?? Output.Create("");
    public CloudProvider Provider => CloudProvider.AWS;

    public Output<string> ServiceUrl => _service?.ServiceUrl ?? Output.Create("");

    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;

    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }

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

    public void AddPolicyStatement(IPolicyStatement statement)
    {
        _customPolicyStatements.Add(statement);
    }

    public void Build()
    {
        // Aggregate all policy statements for the instance role
        var allInstancePolicies = new List<IPolicyStatement>();
        // Secrets access
        foreach (var secret in RequiredSecrets)
        {
            allInstancePolicies.AddRange(secret.GetPolicy(SecretPermissions.Read));
        }
        // Custom policy statements
        if (_customPolicyStatements.Any())
        {
            allInstancePolicies.AddRange(_customPolicyStatements);
        }
        // Create instance role for the running service (for secrets access)
        _instanceRole = new Pulumi.Aws.Iam.Role($"{_name}-instance-role", new Pulumi.Aws.Iam.RoleArgs
        {
            AssumeRolePolicy = @"{
                ""Version"": ""2012-10-17"",
                ""Statement"": [{
                    ""Effect"": ""Allow"",
                    ""Principal"": {
                        ""Service"": ""tasks.apprunner.amazonaws.com""
                    },
                    ""Action"": ""sts:AssumeRole""
                }]
            }"
        });
        if (allInstancePolicies.Any())
        {
            var instancePolicyDocument = CreatePolicyDocument(allInstancePolicies);
            new Pulumi.Aws.Iam.RolePolicy($"{_name}-instance-policy", new Pulumi.Aws.Iam.RolePolicyArgs
            {
                Role = _instanceRole.Name,
                Policy = instancePolicyDocument
            });
        }
        // Aggregate all policy statements for the access role (ECR)
        var ecrPolicies = ContainerRegistry.GetPolicy(
            ContainerRegistryPermissions.Pull,
            ContainerRegistryPermissions.GetAuthToken
        );
        _accessRole = new Pulumi.Aws.Iam.Role($"{_name}-access-role", new Pulumi.Aws.Iam.RoleArgs
        {
            AssumeRolePolicy = @"{
                ""Version"": ""2012-10-17"",
                ""Statement"": [{
                    ""Effect"": ""Allow"",
                    ""Principal"": {
                        ""Service"": ""build.apprunner.amazonaws.com""
                    },
                    ""Action"": ""sts:AssumeRole""
                }]
            }"
        });
        if (ecrPolicies.Any())
        {
            var ecrPolicyDocument = CreatePolicyDocument(ecrPolicies);
            new Pulumi.Aws.Iam.RolePolicy($"{_name}-access-policy", new Pulumi.Aws.Iam.RolePolicyArgs
            {
                Role = _accessRole.Name,
                Policy = ecrPolicyDocument
            });
        }
        // Create or update AutoScalingConfiguration with MinCount/MaxCount
        _autoScalingConfig = new Pulumi.Aws.AppRunner.AutoScalingConfigurationVersion($"{_name}-autoscaling", new Pulumi.Aws.AppRunner.AutoScalingConfigurationVersionArgs()
        {
            MaxConcurrency = MaxCount,
        });
        // NOW create the App Runner service with the roles and autoscaling config attached
        CreateAppRunnerService();
    }

    private void CreateAppRunnerService()
    {
        var imageIdentifier = _containerRegistry is AwsContainerRegistry awsRegistry
            ? awsRegistry.RegistryUrlOutput.Apply(url => $"{url}:{_imageTag}")
            : Output.Create($"PLACEHOLDER_REGISTRY_URL:{_imageTag}");

        // Convert secrets to proper InputMap format
        var secretsMap = new InputMap<string>();
        foreach (var secret in RequiredSecrets)
        {
            var secretKey = $"SECRET_{secret.Name.ToUpper().Replace("-", "_")}";
            secretsMap.Add(secretKey, secret.Arn);
        }

        _service = new Pulumi.Aws.AppRunner.Service(_name, new Pulumi.Aws.AppRunner.ServiceArgs
        {
            ServiceName = _name,
            SourceConfiguration = new Pulumi.Aws.AppRunner.Inputs.ServiceSourceConfigurationArgs
            {
                ImageRepository = new Pulumi.Aws.AppRunner.Inputs.ServiceSourceConfigurationImageRepositoryArgs
                {
                    ImageConfiguration = new Pulumi.Aws.AppRunner.Inputs.ServiceSourceConfigurationImageRepositoryImageConfigurationArgs
                    {
                        Port = "8080",
                        RuntimeEnvironmentVariables = _environmentVariables,
                        RuntimeEnvironmentSecrets = secretsMap
                    },
                    ImageIdentifier = imageIdentifier,
                    ImageRepositoryType = "ECR"
                },
                AutoDeploymentsEnabled = true
            },
            InstanceConfiguration = new Pulumi.Aws.AppRunner.Inputs.ServiceInstanceConfigurationArgs
            {
                InstanceRoleArn = _instanceRole?.Arn,
                Cpu = "256",
                Memory = "512"
            },
            AutoScalingConfigurationArn = _autoScalingConfig?.Arn,
            HealthCheckConfiguration = HealthCheckConfiguration is not null ? new Pulumi.Aws.AppRunner.Inputs.ServiceHealthCheckConfigurationArgs
            {
                Path = HealthCheckConfiguration.Path,
                Interval = HealthCheckConfiguration.IntervalSeconds,
                Timeout = HealthCheckConfiguration.TimeoutSeconds,
                HealthyThreshold = HealthCheckConfiguration.HealthyThreshold,
                UnhealthyThreshold = HealthCheckConfiguration.UnhealthyThreshold
            } : null,
            ObservabilityConfiguration = null // placeholder
        });
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

            return System.Text.Json.JsonSerializer.Serialize(policy);
        });
    }


    public void Start() { /* App Runner auto-starts */ }
    public void Stop() { /* Implementation for pausing service */ }
    public void Scale(int replicas) { /* App Runner auto-scales, but can set min/max */ }

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
                        Actions = new[] { "apprunner:DescribeService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.Start:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "apprunner:ResumeService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.Stop:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "apprunner:PauseService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "logs:GetLogEvents", "logs:DescribeLogStreams" },
                        Resources = new Output<string>[] { Output.Create($"arn:aws:logs:*:*:log-group:/aws/apprunner/{Name}/*") }
                    });
                    break;
                case ComputePermissions.Scale:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "apprunner:UpdateService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
            }
        }

        return statements;
    }
}