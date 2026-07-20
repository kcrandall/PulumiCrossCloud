using System.Globalization;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// Infrastructure shared by the two ECS backends. Express Mode and the standard service differ
/// in how they run and expose a task, but both are Fargate tasks on an ECS cluster and need the
/// same cluster, execution role, task role, log group, and task-size rules.
/// </summary>
public partial class AwsFargateCompute
{
    private const string EcsTaskExecutionPolicyArn =
        "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy";

    private Pulumi.Aws.Ecs.Cluster? _ecsCluster;
    private Pulumi.Aws.Iam.Role? _executionRole;
    private Pulumi.Aws.Iam.Role? _taskRole;
    private Pulumi.Aws.CloudWatch.LogGroup? _ecsLogGroup;

    /// <summary>
    /// The cluster, roles, and log group both ECS backends build their service on top of.
    /// </summary>
    private sealed record EcsFoundation(
        Input<string> Cluster,
        Output<string> ClusterName,
        Pulumi.Aws.Iam.Role ExecutionRole,
        Pulumi.Aws.Iam.Role? TaskRole,
        Pulumi.Aws.CloudWatch.LogGroup LogGroup,
        List<Resource> Dependencies
    );

    /// <summary>
    /// Creates or resolves the cluster and creates the execution role, optional task role, and
    /// log group. A task role is created when the workload has custom policy statements or when
    /// <paramref name="additionalTaskStatements"/> supplies any.
    /// </summary>
    private EcsFoundation CreateEcsFoundation(
        IEnumerable<IPolicyStatement>? additionalTaskStatements = null
    )
    {
        var resourceOptions = new CustomResourceOptions { Provider = _provider };
        var dependencies = new List<Resource>();

        Input<string> cluster;
        Output<string> clusterName;
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
            clusterName = _ecsCluster.Name;
            dependencies.Add(_ecsCluster);
        }
        else
        {
            cluster = _options.ClusterName;
            clusterName = Output.Create(_options.ClusterName).Apply(GetClusterShortName);
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
        dependencies.Add(_executionRole);
        dependencies.Add(
            new Pulumi.Aws.Iam.RolePolicyAttachment(
                $"{Name}-execution-managed-policy",
                new Pulumi.Aws.Iam.RolePolicyAttachmentArgs
                {
                    Role = _executionRole.Name,
                    PolicyArn = EcsTaskExecutionPolicyArn,
                },
                resourceOptions
            )
        );

        if (executionPolicyStatements.Count > 0)
            dependencies.Add(
                new Pulumi.Aws.Iam.RolePolicy(
                    $"{Name}-execution-policy",
                    new Pulumi.Aws.Iam.RolePolicyArgs
                    {
                        Role = _executionRole.Name,
                        Policy = CreatePolicyDocument(executionPolicyStatements),
                    },
                    resourceOptions
                )
            );

        var taskStatements = new List<IPolicyStatement>(_customPolicyStatements);
        if (additionalTaskStatements is not null)
            taskStatements.AddRange(additionalTaskStatements);

        if (taskStatements.Count > 0)
        {
            _taskRole = CreateRole($"{Name}-task-role", "ecs-tasks.amazonaws.com");
            dependencies.Add(_taskRole);
            dependencies.Add(
                new Pulumi.Aws.Iam.RolePolicy(
                    $"{Name}-task-policy",
                    new Pulumi.Aws.Iam.RolePolicyArgs
                    {
                        Role = _taskRole.Name,
                        Policy = CreatePolicyDocument(taskStatements),
                    },
                    resourceOptions
                )
            );
        }

        _ecsLogGroup = new Pulumi.Aws.CloudWatch.LogGroup(
            $"{Name}-logs",
            new Pulumi.Aws.CloudWatch.LogGroupArgs
            {
                Name = $"/aws/ecs/{Name}",
                RetentionInDays = 30,
                Tags = ToInputMap(_options.Tags),
            },
            resourceOptions
        );
        dependencies.Add(_ecsLogGroup);

        return new EcsFoundation(
            cluster,
            clusterName,
            _executionRole,
            _taskRole,
            _ecsLogGroup,
            dependencies
        );
    }

    /// <summary>
    /// Resolves the configured CPU and memory into the units ECS expects, rejecting combinations
    /// Fargate does not offer. Both values must be supplied together so the pair can be checked.
    /// </summary>
    private (int? CpuUnits, int? MemoryMebibytes) ResolveEcsTaskSize()
    {
        if ((CpuLimit is null) != (MemoryLimit is null))
            throw new InvalidOperationException(
                "ECS Fargate requires CpuLimit and MemoryLimit to be set together so their task-size combination can be validated."
            );

        int? cpuUnits = CpuLimit is null ? null : ParseFargateCpu(CpuLimit);
        int? memoryMebibytes = MemoryLimit is null ? null : ParseFargateMemory(MemoryLimit);
        if (cpuUnits is not null && memoryMebibytes is not null)
            ValidateFargateTaskSize(cpuUnits.Value, memoryMebibytes.Value);

        return (cpuUnits, memoryMebibytes);
    }

    private void AddServiceArnPolicyStatement(
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

    /// <summary>
    /// Both ECS backends log to <c>/aws/ecs/{name}</c>, so they read logs the same way.
    /// </summary>
    private IPolicyStatement CreateEcsLogsPolicyStatement() =>
        new AwsPolicyStatement
        {
            Effect = "Allow",
            Actions = ["logs:GetLogEvents", "logs:DescribeLogStreams"],
            Resources = [Output.Create($"arn:aws:logs:*:*:log-group:/aws/ecs/{Name}:*")],
        };

    private static string GetClusterShortName(string clusterNameOrArn) =>
        clusterNameOrArn.Contains('/', StringComparison.Ordinal)
            ? clusterNameOrArn[(clusterNameOrArn.LastIndexOf('/') + 1)..]
            : clusterNameOrArn;

    private static int ParseFargateCpu(string cpuLimit)
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

    private static int ParseFargateMemory(string memoryLimit)
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

    private static void ValidateFargateTaskSize(int cpuUnits, int memoryMebibytes)
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
