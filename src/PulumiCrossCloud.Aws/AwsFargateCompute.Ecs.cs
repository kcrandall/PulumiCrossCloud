using System.Globalization;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public partial class AwsFargateCompute
{
    private const string EcsTaskExecutionPolicyArn =
        "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy";
    private const string EcsExpressInfrastructurePolicyArn =
        "arn:aws:iam::aws:policy/service-role/AmazonECSInfrastructureRoleforExpressGatewayServices";

    private Pulumi.Aws.Ecs.Cluster? _ecsCluster;
    private Pulumi.Aws.Ecs.ExpressGatewayService? _ecsService;
    private Pulumi.Aws.Iam.Role? _executionRole;
    private Pulumi.Aws.Iam.Role? _infrastructureRole;
    private Pulumi.Aws.Iam.Role? _taskRole;

    partial void BuildEcsService()
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

    partial void AddEcsPolicy(List<IPolicyStatement> statements, ComputePermissions permission)
    {
        switch (permission)
        {
            case ComputePermissions.GetStatus:
                AddEcsPolicyStatement(statements, "ecs:DescribeExpressGatewayService");
                break;
            case ComputePermissions.Start:
            case ComputePermissions.Stop:
            case ComputePermissions.Restart:
            case ComputePermissions.Scale:
                AddEcsPolicyStatement(statements, "ecs:UpdateExpressGatewayService");
                break;
            case ComputePermissions.Terminate:
                AddEcsPolicyStatement(statements, "ecs:DeleteExpressGatewayService");
                break;
            case ComputePermissions.Connect:
                // Express Gateway services do not expose ECS Exec enablement.
                break;
            case ComputePermissions.UpdateConfiguration:
                AddEcsPolicyStatement(statements, "ecs:UpdateExpressGatewayService");
                break;
            case ComputePermissions.GetLogs:
                statements.Add(
                    new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["logs:GetLogEvents", "logs:DescribeLogStreams"],
                        Resources =
                        [
                            Output.Create($"arn:aws:logs:*:*:log-group:/aws/ecs/{Name}:*"),
                        ],
                    }
                );
                break;
        }
    }

    private void AddEcsPolicyStatement(
        List<IPolicyStatement> statements,
        params string[] actions
    ) =>
        statements.Add(
            new AwsPolicyStatement
            {
                Effect = "Allow",
                Actions = actions,
                Resources = [Arn],
            }
        );

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
            16384 => memoryMebibytes is >= 32768 and <= 122880 && memoryMebibytes % 8192 == 0,
            32768 => memoryMebibytes is 61440 or 122880 or 249856,
            _ => false,
        };

        if (!valid)
            throw new InvalidOperationException(
                $"The ECS Fargate task size {cpuUnits / 1024m:0.##} vCPU / "
                    + $"{memoryMebibytes}Mi is not a supported CPU and memory combination."
            );
    }
}
