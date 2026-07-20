using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// Runs a container on AWS Fargate using either Amazon ECS or Amazon EKS.
/// </summary>
public partial class AwsFargateCompute : ICompute
{
    private const string EcsTaskExecutionPolicyArn =
        "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy";
    private const string EcsExpressInfrastructurePolicyArn =
        "arn:aws:iam::aws:policy/service-role/AmazonECSInfrastructureRoleforExpressGatewayServices";

    private readonly List<(ISecret Secret, string? EnvironmentVariableName)> _requiredSecrets = [];
    private readonly Dictionary<string, string> _environmentVariables = [];
    private readonly Dictionary<string, Output<string>> _environmentVariablesOutput = [];
    private readonly List<IPolicyStatement> _customPolicyStatements = [];
    private readonly List<Func<ICompute, Task>> _onHealthyActions = [];
    private readonly List<Func<ICompute, Task>> _onReadyActions = [];
    private readonly List<Resource> _dependencies = [];
    private readonly string _imageTag;
    private readonly IContainerImage? _containerImage;
    private readonly AwsFargateOptions _options;
    private readonly Pulumi.Aws.Provider? _provider;

    private Pulumi.Aws.Ecs.Cluster? _ecsCluster;
    private Pulumi.Aws.Ecs.ExpressGatewayService? _ecsService;
    private Pulumi.Aws.Iam.Role? _executionRole;
    private Pulumi.Aws.Iam.Role? _infrastructureRole;
    private Pulumi.Aws.Iam.Role? _taskRole;
    private Output<string> _arn = Output.Create(string.Empty);
    private Output<string> _serviceUrl = Output.Create(string.Empty);
    private Output<string> _dnsTarget = Output.Create(string.Empty);
    private bool _built;

    public AwsFargateCompute(
        string name,
        IContainerRegistry containerRegistry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        AwsFargateOptions? options = null,
        Pulumi.Aws.Provider? provider = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageTag);
        ArgumentNullException.ThrowIfNull(containerRegistry);

        if (containerRegistry.Provider != CloudProvider.AWS)
            throw new ArgumentException(
                "AWS Fargate requires an AWS container registry.",
                nameof(containerRegistry)
            );
        if (
            containerImage is not null
            && !ReferenceEquals(containerImage.Registry, containerRegistry)
        )
            throw new ArgumentException(
                "containerImage must have been created from the containerRegistry passed to AWS Fargate.",
                nameof(containerImage)
            );

        if (string.IsNullOrWhiteSpace(imageName) && containerImage is null)
            throw new ArgumentException(
                "Either imageName or containerImage must be provided for AWS Fargate compute.",
                nameof(imageName)
            );
        if (
            containerImage is null
            && !string.Equals(imageName, containerRegistry.Name, StringComparison.Ordinal)
        )
            throw new ArgumentException(
                $"AWS container registry '{containerRegistry.Name}' represents one ECR repository. "
                    + "imageName must match registry.Name, or containerImage must be supplied from CreateContainerImage/PushImage.",
                nameof(imageName)
            );

        Name = name;
        ContainerRegistry = containerRegistry;
        _imageTag = imageTag;
        _containerImage = containerImage;
        _options = options ?? new AwsFargateOptions();
        _provider = provider;
    }

    public string Name { get; }
    public ComputeServiceType ServiceType => ComputeServiceType.Fargate;
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets.Select(item => item.Secret);
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
    public Output<string> Arn => _arn;
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> ServiceUrl => _serviceUrl;
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    public string? MemoryLimit { get; set; }
    public string? CpuLimit { get; set; }
    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
    public IList<PortForwardingConfiguration> PortForwardings { get; set; } =
    [
        new PortForwardingConfiguration { ContainerPort = 8080 },
    ];

    public void AddRequiredSecret(ISecret secret, string? environmentVariableName = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        _requiredSecrets.Add((secret, environmentVariableName));
    }

    public void AddEnvironmentVariable(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _environmentVariables[name] = value;
    }

    public void AddEnvironmentVariable(string name, Output<string> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _environmentVariablesOutput[name] = value;
    }

    public void AddEnvironmentVariables(IDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        foreach (var (name, value) in variables)
            AddEnvironmentVariable(name, value);
    }

    public void AddPolicyStatement(IPolicyStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        _customPolicyStatements.Add(statement);
    }

    public ComputeConfigurationResponse GetConfiguration() =>
        new()
        {
            EnvironmentVariables = new Dictionary<string, string>(_environmentVariables),
            EnvironmentVariablesOutput = new Dictionary<string, Output<string>>(
                _environmentVariablesOutput
            ),
            RequiredSecrets = RequiredSecrets.ToArray(),
            SecretMappings = _requiredSecrets
                .GroupBy(item => item.Secret.Name)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().EnvironmentVariableName
                ),
        };

    public void Build()
    {
        if (_built)
            return;

        ValidateCommonConfiguration();

        switch (_options.Backend)
        {
            case AwsFargateBackend.Ecs:
                BuildEcsService();
                break;
            case AwsFargateBackend.Eks:
                BuildEksService();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(_options.Backend),
                    _options.Backend,
                    "Unsupported AWS Fargate backend."
                );
        }

        _built = true;
        foreach (var action in _onReadyActions)
            ScheduleReadyAction(action);
        foreach (var action in _onHealthyActions)
            ScheduleHealthyAction(action);
    }

    private void BuildEcsService()
    {
        if (_options.SubnetIds is { Length: 1 })
            throw new ArgumentException(
                "ECS Express Mode requires at least two subnet IDs when custom networking is configured.",
                nameof(_options.SubnetIds)
            );
        if ((CpuLimit is null) != (MemoryLimit is null))
            throw new InvalidOperationException(
                "ECS Fargate requires CpuLimit and MemoryLimit to be set together so their task-size combination can be validated."
            );

        int? cpuUnits = CpuLimit is null ? null : ParseExpressCpu(CpuLimit);
        int? memoryMebibytes = MemoryLimit is null ? null : ParseExpressMemory(MemoryLimit);
        if (cpuUnits is not null && memoryMebibytes is not null)
            ValidateExpressTaskSize(cpuUnits.Value, memoryMebibytes.Value);

        var resourceOptions = new CustomResourceOptions { Provider = _provider };
        Input<string> cluster;

        if (string.IsNullOrWhiteSpace(_options.ClusterName))
        {
            _ecsCluster = new Pulumi.Aws.Ecs.Cluster(
                $"{Name}-cluster",
                new Pulumi.Aws.Ecs.ClusterArgs
                {
                    Name = $"{Name}-cluster",
                    Tags = ToInputMap(_options.Tags),
                },
                resourceOptions
            );
            cluster = _ecsCluster.Arn;
            _dependencies.Add(_ecsCluster);
        }
        else
        {
            cluster = _options.ClusterName;
        }

        var executionPolicyStatements = new List<IPolicyStatement>();
        executionPolicyStatements.AddRange(
            ContainerRegistry.GetPolicy(
                ContainerRegistryPermissions.Pull,
                ContainerRegistryPermissions.GetAuthToken
            )
        );
        foreach (var secret in RequiredSecrets)
            executionPolicyStatements.AddRange(secret.GetPolicy(SecretPermissions.Read));

        _executionRole = CreateRole($"{Name}-execution-role", "ecs-tasks.amazonaws.com");
        var executionAttachment = new Pulumi.Aws.Iam.RolePolicyAttachment(
            $"{Name}-execution-managed-policy",
            new Pulumi.Aws.Iam.RolePolicyAttachmentArgs
            {
                Role = _executionRole.Name,
                PolicyArn = EcsTaskExecutionPolicyArn,
            },
            resourceOptions
        );

        Pulumi.Aws.Iam.RolePolicy? executionPolicy = null;
        if (executionPolicyStatements.Count > 0)
        {
            executionPolicy = new Pulumi.Aws.Iam.RolePolicy(
                $"{Name}-execution-policy",
                new Pulumi.Aws.Iam.RolePolicyArgs
                {
                    Role = _executionRole.Name,
                    Policy = CreatePolicyDocument(executionPolicyStatements),
                },
                resourceOptions
            );
        }

        _infrastructureRole = CreateRole($"{Name}-infrastructure-role", "ecs.amazonaws.com");
        var infrastructureAttachment = new Pulumi.Aws.Iam.RolePolicyAttachment(
            $"{Name}-infrastructure-managed-policy",
            new Pulumi.Aws.Iam.RolePolicyAttachmentArgs
            {
                Role = _infrastructureRole.Name,
                PolicyArn = EcsExpressInfrastructurePolicyArn,
            },
            resourceOptions
        );

        Pulumi.Aws.Iam.RolePolicy? taskPolicy = null;
        if (_customPolicyStatements.Count > 0)
        {
            _taskRole = CreateRole($"{Name}-task-role", "ecs-tasks.amazonaws.com");
            taskPolicy = new Pulumi.Aws.Iam.RolePolicy(
                $"{Name}-task-policy",
                new Pulumi.Aws.Iam.RolePolicyArgs
                {
                    Role = _taskRole.Name,
                    Policy = CreatePolicyDocument(_customPolicyStatements),
                },
                resourceOptions
            );
        }

        var logGroup = new Pulumi.Aws.CloudWatch.LogGroup(
            $"{Name}-logs",
            new Pulumi.Aws.CloudWatch.LogGroupArgs
            {
                Name = $"/aws/ecs/{Name}",
                RetentionInDays = 30,
                Tags = ToInputMap(_options.Tags),
            },
            resourceOptions
        );

        var primaryContainer = CreateEcsPrimaryContainer(logGroup);
        var serviceArgs = new Pulumi.Aws.Ecs.ExpressGatewayServiceArgs
        {
            Cluster = cluster,
            ExecutionRoleArn = _executionRole.Arn,
            InfrastructureRoleArn = _infrastructureRole.Arn,
            PrimaryContainer = primaryContainer,
            ServiceName = Name,
            HealthCheckPath = NormalizeHealthPath(HealthCheckConfiguration?.Path),
            ScalingTargets =
            [
                new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServiceScalingTargetArgs
                {
                    AutoScalingMetric = "AVERAGE_CPU",
                    AutoScalingTargetValue = 60,
                    MinTaskCount = MinCount,
                    MaxTaskCount = MaxCount,
                },
            ],
            Tags = ToInputMap(_options.Tags),
            WaitForSteadyState = _options.WaitForSteadyState,
        };

        if (_taskRole is not null)
            serviceArgs.TaskRoleArn = _taskRole.Arn;
        if (cpuUnits is { } resolvedCpuUnits)
            serviceArgs.Cpu = resolvedCpuUnits.ToString(CultureInfo.InvariantCulture);
        if (memoryMebibytes is { } resolvedMemoryMebibytes)
            serviceArgs.Memory = resolvedMemoryMebibytes.ToString(CultureInfo.InvariantCulture);
        if (_options.SubnetIds is { Length: > 0 } || _options.SecurityGroupIds is { Length: > 0 })
        {
            var networkConfiguration =
                new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServiceNetworkConfigurationArgs();
            if (_options.SubnetIds is { Length: > 0 })
                networkConfiguration.Subnets = _options.SubnetIds;
            if (_options.SecurityGroupIds is { Length: > 0 })
                networkConfiguration.SecurityGroups = _options.SecurityGroupIds;
            serviceArgs.NetworkConfigurations = [networkConfiguration];
        }

        var serviceDependencies = new List<Resource>
        {
            executionAttachment,
            infrastructureAttachment,
            logGroup,
        };
        if (executionPolicy is not null)
            serviceDependencies.Add(executionPolicy);
        if (taskPolicy is not null)
            serviceDependencies.Add(taskPolicy);
        if (_containerImage?.Resource is { } imageResource)
            serviceDependencies.Add(imageResource);
        foreach (var (secret, _) in _requiredSecrets)
            serviceDependencies.Add(secret.ValueResource);

        _ecsService = new Pulumi.Aws.Ecs.ExpressGatewayService(
            $"{Name}-service",
            serviceArgs,
            new CustomResourceOptions
            {
                Provider = _provider,
                DependsOn = serviceDependencies,
            }
        );

        _arn = _ecsService.ServiceArn;
        var endpoint = _ecsService.IngressPaths.Apply(paths =>
            paths.FirstOrDefault(path =>
                    string.Equals(path.AccessType, "PUBLIC", StringComparison.OrdinalIgnoreCase)
                )?.Endpoint
                ?? paths.FirstOrDefault()?.Endpoint
                ?? string.Empty
        );
        _serviceUrl = endpoint.Apply(EnsureUrlScheme);
        _dnsTarget = endpoint.Apply(GetDnsTarget);

        _dependencies.AddRange(serviceDependencies);
        _dependencies.Add(_executionRole);
        _dependencies.Add(_infrastructureRole);
        if (_taskRole is not null)
            _dependencies.Add(_taskRole);
        _dependencies.Add(_ecsService);
    }

    private Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerArgs CreateEcsPrimaryContainer(
        Pulumi.Aws.CloudWatch.LogGroup logGroup
    )
    {
        var containerPort = GetPrimaryPort();
        var environments = new List<
            Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerEnvironmentArgs
        >();
        foreach (var (name, value) in _environmentVariables)
            environments.Add(new() { Name = name, Value = value });
        foreach (var (name, value) in _environmentVariablesOutput)
            environments.Add(new() { Name = name, Value = value });

        var secrets = _requiredSecrets
            .Select(item =>
                new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerSecretArgs
                {
                    Name = GetSecretEnvironmentVariableName(item),
                    ValueFrom = item.Secret.Arn,
                }
            )
            .ToList();

        return new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerArgs
        {
            Image = GetContainerImageUri(),
            ContainerPort = containerPort,
            Environments = environments,
            Secrets = secrets,
            AwsLogsConfigurations =
            [
                new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerAwsLogsConfigurationArgs
                {
                    LogGroup = logGroup.Name,
                    LogStreamPrefix = Name,
                },
            ],
        };
    }

    private Pulumi.Aws.Iam.Role CreateRole(string resourceName, string trustedService) =>
        new(
            resourceName,
            new Pulumi.Aws.Iam.RoleArgs
            {
                AssumeRolePolicy = JsonSerializer.Serialize(
                    new
                    {
                        Version = "2012-10-17",
                        Statement = new[]
                        {
                            new
                            {
                                Effect = "Allow",
                                Principal = new { Service = trustedService },
                                Action = "sts:AssumeRole",
                            },
                        },
                    }
                ),
                Tags = ToInputMap(_options.Tags),
            },
            new CustomResourceOptions { Provider = _provider }
        );

    private Output<string> CreatePolicyDocument(IEnumerable<IPolicyStatement> statements)
    {
        var statementList = statements.ToList();
        var resources = statementList.SelectMany(statement => statement.Resources).ToArray();

        return Output.All(resources).Apply(resolvedResources =>
        {
            var resourceIndex = 0;
            var nativeStatements = new List<object>();

            foreach (var statement in statementList)
            {
                var resourceCount = statement.Resources.Count();
                nativeStatements.Add(
                    new
                    {
                        Effect = statement.Effect,
                        Action = statement.Actions.ToArray(),
                        Resource = resolvedResources
                            .Skip(resourceIndex)
                            .Take(resourceCount)
                            .ToArray(),
                        Condition = statement.Conditions.Count > 0
                            ? statement.Conditions
                            : null,
                    }
                );
                resourceIndex += resourceCount;
            }

            return JsonSerializer.Serialize(
                new { Version = "2012-10-17", Statement = nativeStatements }
            );
        });
    }

    public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names) =>
        names.Select(name =>
            new DnsRecord(DnsRecordType.CNAME, name, _dnsTarget, $"{name}-cname")
        );

    public IEnumerable<Resource> GetDependencies() => _dependencies.Distinct();

    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            if (_options.Backend == AwsFargateBackend.Eks)
            {
                AddEksPolicy(statements, permission);
                continue;
            }

            switch (permission)
            {
                case ComputePermissions.GetStatus:
                    AddPolicy(statements, "ecs:DescribeExpressGatewayService");
                    break;
                case ComputePermissions.Start:
                case ComputePermissions.Stop:
                case ComputePermissions.Restart:
                case ComputePermissions.Scale:
                    AddPolicy(statements, "ecs:UpdateExpressGatewayService");
                    break;
                case ComputePermissions.Terminate:
                    AddPolicy(statements, "ecs:DeleteExpressGatewayService");
                    break;
                case ComputePermissions.Connect:
                    // Express Gateway services do not expose ECS Exec enablement.
                    break;
                case ComputePermissions.UpdateConfiguration:
                    AddPolicy(statements, "ecs:UpdateExpressGatewayService");
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(
                        new AwsPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = ["logs:GetLogEvents", "logs:DescribeLogStreams"],
                            Resources =
                            [
                                Output.Create(
                                    $"arn:aws:logs:*:*:log-group:/aws/ecs/{Name}:*"
                                ),
                            ],
                        }
                    );
                    break;
            }
        }

        return statements;
    }

    private void AddPolicy(List<IPolicyStatement> statements, params string[] actions) =>
        statements.Add(
            new AwsPolicyStatement
            {
                Effect = "Allow",
                Actions = actions,
                Resources = [Arn],
            }
        );

    public void OnReady(params Func<ICompute, Task>[] actions)
    {
        _onReadyActions.AddRange(actions);
        if (_built)
            foreach (var action in actions)
                ScheduleReadyAction(action);
    }

    public void OnHealthy(params Func<ICompute, Task>[] actions)
    {
        _onHealthyActions.AddRange(actions);
        if (_built)
            foreach (var action in actions)
                ScheduleHealthyAction(action);
    }

    private void ScheduleReadyAction(Func<ICompute, Task> action) =>
        ServiceUrl.Apply(async _ => await action(this));

    private void ScheduleHealthyAction(Func<ICompute, Task> action) =>
        ServiceUrl.Apply(async url =>
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            var healthUrl = $"{url.TrimEnd('/')}{NormalizeHealthPath(HealthCheckConfiguration?.Path)}";
            using var http = new HttpClient();
            for (var attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    var response = await http.GetAsync(healthUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        await action(this);
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // The load balancer may not be reachable while the service is converging.
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            Pulumi.Log.Error(
                $"Fargate health check failed for {Name} at {healthUrl} after 5 minutes."
            );
        });

    private void ValidateCommonConfiguration()
    {
        if (MinCount < 0)
            throw new InvalidOperationException("MinCount cannot be negative.");
        if (MaxCount < 1)
            throw new InvalidOperationException("MaxCount must be at least one.");
        if (MaxCount < MinCount)
            throw new InvalidOperationException("MaxCount cannot be less than MinCount.");

        if (PortForwardings.Count == 0)
            PortForwardings.Add(new PortForwardingConfiguration { ContainerPort = 8080 });

        foreach (var forwarding in PortForwardings)
        {
            if (forwarding.ContainerPort is < 1 or > 65535)
                throw new InvalidOperationException(
                    $"Container port {forwarding.ContainerPort} is outside the valid range."
                );
        }
    }

    private int GetPrimaryPort()
    {
        var forwarding = PortForwardings.FirstOrDefault();
        if (forwarding.ContainerPort == 0)
            return 8080;
        if (
            !string.IsNullOrWhiteSpace(forwarding.Protocol)
            && !string.Equals(forwarding.Protocol, "TCP", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException(
                "ECS Express Mode supports TCP/HTTP ingress for the primary container port."
            );

        return forwarding.ContainerPort;
    }

    private static string GetSecretEnvironmentVariableName(
        (ISecret Secret, string? EnvironmentVariableName) item
    ) =>
        item.EnvironmentVariableName
        ?? $"SECRET_{item.Secret.Name.ToUpperInvariant().Replace('-', '_')}";

    private Output<string> GetContainerImageUri()
    {
        if (_containerImage is null)
            return ContainerRegistry.RegistryUrlOutput.Apply(url => $"{url}:{_imageTag}");

        var deploymentResult = _containerImage.DeploymentResult;
        if (deploymentResult is null)
            throw new InvalidOperationException(
                $"Container image '{_containerImage.Name}' must be pushed before building Fargate compute '{Name}'."
            );

        return deploymentResult.Apply(result =>
        {
            if (string.IsNullOrWhiteSpace(result.ImageUrl))
                throw new InvalidOperationException(
                    $"Container image '{_containerImage.Name}' did not produce an image URL."
                );
            return result.ImageUrl;
        });
    }

    private static InputMap<string> ToInputMap(IDictionary<string, string>? values)
    {
        var result = new InputMap<string>();
        if (values is null)
            return result;
        foreach (var (key, value) in values)
            result.Add(key, value);
        return result;
    }

    private static string NormalizeHealthPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string EnsureUrlScheme(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;
        return endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? endpoint.TrimEnd('/')
            : $"https://{endpoint.TrimEnd('/')}";
    }

    private static string GetDnsTarget(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;
        var url = EnsureUrlScheme(endpoint);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : endpoint;
    }

    private static int ParseExpressCpu(string cpuLimit)
    {
        if (
            !decimal.TryParse(
                cpuLimit,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var vCpu
            )
        )
            throw new InvalidOperationException($"Invalid CPU limit '{cpuLimit}'.");

        var exactUnits = vCpu * 1024;
        if (exactUnits != decimal.Truncate(exactUnits))
            throw new InvalidOperationException(
                $"CPU limit '{cpuLimit}' does not resolve to a whole number of CPU units."
            );
        var units = checked((int)exactUnits);
        if (units is not (256 or 512 or 1024 or 2048 or 4096 or 8192 or 16384 or 32768))
            throw new InvalidOperationException(
                "ECS Fargate CPU must be 0.25, 0.5, 1, 2, 4, 8, 16, or 32 vCPU."
            );
        return units;
    }

    private static int ParseExpressMemory(string memoryLimit)
    {
        var value = memoryLimit.Trim();
        decimal multiplier = 1;
        if (value.EndsWith("Gi", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024;
            value = value[..^2];
        }
        else if (value.EndsWith("Mi", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
        }
        else if (value.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024;
            value = value[..^2];
        }
        else if (value.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
        }

        if (
            !decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount
            )
        )
            throw new InvalidOperationException($"Invalid memory limit '{memoryLimit}'.");

        var exactMebibytes = amount * multiplier;
        if (exactMebibytes != decimal.Truncate(exactMebibytes))
            throw new InvalidOperationException(
                $"Memory limit '{memoryLimit}' does not resolve to a whole number of MiB."
            );
        var mebibytes = checked((int)exactMebibytes);
        if (mebibytes is < 512 or > 249856)
            throw new InvalidOperationException(
                "ECS Fargate memory must be between 512Mi and 244Gi."
            );
        return mebibytes;
    }

    private static void ValidateExpressTaskSize(int cpuUnits, int memoryMebibytes)
    {
        var valid = cpuUnits switch
        {
            256 => memoryMebibytes is 512 or 1024 or 2048,
            512 => memoryMebibytes is >= 1024 and <= 4096 && memoryMebibytes % 1024 == 0,
            1024 => memoryMebibytes is >= 2048 and <= 8192 && memoryMebibytes % 1024 == 0,
            2048 => memoryMebibytes is >= 4096 and <= 16384 && memoryMebibytes % 1024 == 0,
            4096 => memoryMebibytes is >= 8192 and <= 30720 && memoryMebibytes % 1024 == 0,
            8192 => memoryMebibytes is >= 16384 and <= 61440 && memoryMebibytes % 4096 == 0,
            16384 =>
                memoryMebibytes is >= 32768 and <= 122880 && memoryMebibytes % 8192 == 0,
            32768 => memoryMebibytes is 61440 or 122880 or 249856,
            _ => false,
        };

        if (!valid)
            throw new InvalidOperationException(
                $"The ECS Fargate task size {cpuUnits / 1024m:0.##} vCPU / "
                    + $"{memoryMebibytes}Mi is not a supported CPU and memory combination."
            );
    }

    partial void BuildEksService();
    partial void AddEksPolicy(List<IPolicyStatement> statements, ComputePermissions permission);
}
