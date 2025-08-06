using Pulumi;
using PulumiCrossCloud.Core;
using System.Net.Http;
using System.Threading.Tasks;

namespace PulumiCrossCloud.Aws;

public class AwsAppRunnerCompute : ICompute
{
    private readonly List<(ISecret secret, string? envVarName)> _requiredSecrets = [];
    private readonly Dictionary<string, string> _environmentVariables;
    private Pulumi.Aws.Iam.Role? _instanceRole;
    private Pulumi.Aws.Iam.Role? _accessRole;
    private Pulumi.Aws.AppRunner.Service? _service;
    private readonly string _name;
    private readonly IContainerRegistry _containerRegistry;
    private readonly string _imageName;
    private readonly string _imageTag;
    private readonly IContainerImage? _containerImage;
    private readonly List<IPolicyStatement> _customPolicyStatements = new();
    private Pulumi.Aws.AppRunner.AutoScalingConfigurationVersion? _autoScalingConfig;

    private readonly List<Func<ICompute, Task>> _onHealthyActions = [];
    private readonly List<Func<ICompute, Task>> _onReadyActions = [];

    public AwsAppRunnerCompute(string name, IContainerRegistry containerRegistry, string? imageName = null, string imageTag = "latest", IContainerImage? containerImage = null)
    {
        _name = name;
        _containerRegistry = containerRegistry;
        _imageName = imageName;
        _imageTag = imageTag;
        if(imageName is null && containerImage is not null)
            _imageName = containerImage.Name;
        else if(imageName is null && containerImage is null)
            throw new ArgumentException("Either imageName or containerImage must be provided for GCP Cloud Run compute resource.");
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.AppRunner;
        _environmentVariables = new Dictionary<string, string>();

        // Don't create the service yet - wait until ConfigurePermissions is called
    }

    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets.Select(x => x.secret);
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
    public Output<string> Arn => _service?.Arn ?? Output.Create("");
    public CloudProvider Provider => CloudProvider.AWS;
    public IList<PortForwardingConfiguration> PortForwardings { get; set; } = new List<PortForwardingConfiguration> { new PortForwardingConfiguration { ContainerPort = 8080 } };

    public Output<string> ServiceUrl => _service.ServiceUrl;

    public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names)
    {
        // For AWS App Runner, a CNAME record is needed to map a custom domain to the App Runner service URL.
        // Example: CNAME app.example.com -> <service>.awsapprunner.com
        // For each provided domain name, return a CNAME record pointing to the App Runner service URL
        return names.Select(name => new DnsRecord(DnsRecordType.CNAME, name, ServiceUrl)).ToList();
    }

    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;

    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }

    public void AddRequiredSecret(ISecret secret, string? environmentVariableName = null)
    {
        _requiredSecrets.Add((secret, environmentVariableName));
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
        foreach (var (secret, envVarName) in _requiredSecrets)
        {
            var secretKey = envVarName ?? $"SECRET_{secret.Name.ToUpper().Replace("-", "_")}";
            secretsMap.Add(secretKey, secret.Arn);
        }
        // Collect all secret resources for dependsOn
        var dependsOn = new List<Resource>();
        foreach (var (secret, _) in _requiredSecrets)
        {
            if (secret.Resource is not null)
            {
                dependsOn.Add(secret.Resource);
            }
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
        }, new CustomResourceOptions { DependsOn = dependsOn });
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
    public void Deploy() {  }
    public void Stop() { /* Implementation for pausing service */ }
    public void Scale(int replicas) { /* App Runner auto-scales, but can set min/max */ }

    public void OnReady(params Func<ICompute, Task>[] actions)
    {
        _onReadyActions.AddRange(actions);
        // Register ready logic to run after deployment
        ServiceUrl.Apply(async url =>
        {
            foreach (var action in _onReadyActions)
            {
                await action(this);
            }
        });
    }

    public void OnHealthy(params Func<ICompute, Task>[] actions)
    {
        _onHealthyActions.AddRange(actions);
        ServiceUrl.Apply(async url =>
        {
            var healthPath = HealthCheckConfiguration?.Path ?? "";
            var healthUrl = $"{url}{(healthPath.StartsWith("/") ? healthPath : "/" + healthPath)}";
            using var http = new HttpClient();
            var healthy = false;
            for (int i = 0; i < 60; i++)
            {
                try
                {
                    var response = await http.GetAsync(healthUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        healthy = true;
                        foreach (var action in _onHealthyActions)
                        {
                            await action(this);
                        }
                        break;
                    }
                }
                catch
                {
                    // ignore connection failures, retry
                }
                await Task.Delay(5000);
            }
            if (!healthy)
            {
                Pulumi.Log.Error($"App Runner health check failed for {Name} at {healthUrl} after 5 minutes.");
            }
        });
    }

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